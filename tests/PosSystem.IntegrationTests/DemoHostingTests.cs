using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace PosSystem.IntegrationTests;

public sealed class DemoHostingTests
{
    [PgFact]
    public async Task Combined_host_serves_client_routes_without_hiding_missing_API_or_assets()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        var directory = Path.Combine(Path.GetTempPath(), "pos-client-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string index = "<!doctype html><html><body>POS client test</body></html>";
            await File.WriteAllTextAsync(Path.Combine(directory, "index.html"), index);
            await File.WriteAllTextAsync(Path.Combine(directory, "runtime-config.js"),
                "window.POS_RUNTIME_CONFIG = Object.freeze({apiOrigin:window.location.origin});");
            await using var host = await ApiProcess.StartAsync(database, new Dictionary<string, string?>
            {
                ["Web__ServeClient"] = "true", ["Web__Directory"] = directory,
                ["Web__PublicOrigin"] = "https://pos-demo.example"
            });
            Assert.Equal(index, await host.Client.GetStringAsync("/"));
            Assert.Equal(index, await host.Client.GetStringAsync("/pos"));
            Assert.Contains("window.location.origin", await host.Client.GetStringAsync("/runtime-config.js"));
            Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/api/not-a-route")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/missing.js")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await host.Client.PostAsync("/not-a-route", null)).StatusCode);
            host.Client.DefaultRequestHeaders.Remove("Origin");
            host.Client.DefaultRequestHeaders.Add("Origin", "https://pos-demo.example");
            await host.LoginAsync();
        }
        finally { Directory.Delete(directory, true); }
    }

    [PgFact]
    public async Task Database_images_survive_API_restart_without_a_persistent_disk_and_delete_cleanly()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        var directory = Path.Combine(Path.GetTempPath(), "pos-no-disk-" + Guid.NewGuid().ToString("N"));
        var settings = new Dictionary<string, string?>
        {
            ["ProductImages__Storage"] = "Database", ["ProductImages__Directory"] = directory
        };
        // A real 1x1 PNG. Image bytes are deliberately separate from catalog/audit metadata.
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        int productId, imageId;
        await using (var host = await ApiProcess.StartAsync(database, settings))
        {
            await host.LoginAsync();
            var category = await host.Client.PostAsJsonAsync("/api/categories", new { name = "Demo images" });
            await ApiProcess.EnsureSuccessAsync(category);
            var categoryId = (await category.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            var product = await host.Client.PostAsJsonAsync("/api/products", new
            {
                sku = "DEMO-IMAGE", name = "Image item", categoryId, salePrice = 10, stockQuantity = 1, isActive = true
            });
            await ApiProcess.EnsureSuccessAsync(product);
            productId = (await product.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            using var multipart = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            multipart.Add(file, "file", "demo.png");
            var uploaded = await host.Client.PostAsync($"/api/products/{productId}/images", multipart);
            await ApiProcess.EnsureSuccessAsync(uploaded);
            imageId = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            Assert.False(Directory.Exists(directory));
        }
        await using (var restarted = await ApiProcess.StartAsync(database, settings))
        {
            var download = await restarted.Client.GetAsync($"/api/products/{productId}/images/{imageId}");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);
            Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
            await restarted.LoginAsync();
            var deleted = await restarted.Client.DeleteAsync($"/api/products/{productId}/images/{imageId}");
            await ApiProcess.EnsureSuccessAsync(deleted);
            Assert.Equal(HttpStatusCode.NotFound,
                (await restarted.Client.GetAsync($"/api/products/{productId}/images/{imageId}")).StatusCode);
        }
        await using var verify = database.Open();
        Assert.Empty(await verify.ProductImageContents.ToListAsync());
        Assert.DoesNotContain(await verify.AuditLogs.ToListAsync(), log =>
            log.EntityName == "ProductImageContent" || (log.NewValues?.Contains(Convert.ToBase64String(bytes)) ?? false));
    }
}
