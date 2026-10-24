using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace PosSystem.IntegrationTests;

public sealed class ApiWorkflowTests
{
    [PgFact]
    public async Task Real_api_migrates_starts_authenticates_refreshes_and_revokes_access()
    {
        await using var database = await PostgresDatabase.CreateAsync(migrate: false);
        await using var api = await ApiProcess.StartAsync(database);
        var (_, cookie) = await api.LoginAsync();
        using (var me = await api.Client.GetAsync("/api/auth/me")) await ApiProcess.EnsureSuccessAsync(me);
        using (var swagger = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { username = "admin", password = ApiProcess.Password })
        })
        {
            swagger.Headers.Add("Origin", api.Client.BaseAddress!.GetLeftPart(UriPartial.Authority));
            using var response = await api.Client.SendAsync(swagger);
            await ApiProcess.EnsureSuccessAsync(response);
        }

        using (var hostile = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh"))
        {
            hostile.Headers.Add("Origin", "https://hostile.example");
            hostile.Headers.Add("Cookie", cookie);
            using var response = await api.Client.SendAsync(hostile);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using (var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh"))
        {
            refresh.Headers.Add("Cookie", cookie);
            using var response = await api.Client.SendAsync(refresh);
            await ApiProcess.EnsureSuccessAsync(response);
            var rotated = response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
            Assert.NotEqual(cookie, rotated);
            using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
            logout.Headers.Add("Cookie", rotated);
            using var logoutResponse = await api.Client.SendAsync(logout);
            Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
            using var reused = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
            reused.Headers.Add("Cookie", rotated);
            using var reusedResponse = await api.Client.SendAsync(reused);
            Assert.Equal(HttpStatusCode.Unauthorized, reusedResponse.StatusCode);
        }

        await using (var db = database.Open())
        {
            var user = await db.Users.SingleAsync();
            user.IsActive = false;
            await db.SaveChangesAsync();
        }
        using var revoked = await api.Client.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [PgFact]
    public async Task Real_http_checkout_return_and_shift_close_reconcile_cash_and_support_exact_sku()
    {
        await using var database = await PostgresDatabase.CreateAsync(migrate: false);
        await using var api = await ApiProcess.StartAsync(database);
        await api.LoginAsync();
        var categoryId = (await GetJsonAsync(api, "/api/categories"))[0].GetProperty("id").GetInt32();
        using var productResponse = await api.Client.PostAsJsonAsync("/api/products", new
        {
            sku = "SCAN-ME", name = "Integration product", categoryId, salePrice = 10m,
            stockQuantity = 5, minStockThreshold = 4, isActive = true
        });
        await ApiProcess.EnsureSuccessAsync(productResponse);
        var product = await productResponse.Content.ReadFromJsonAsync<JsonElement>();
        var productId = product.GetProperty("id").GetInt32();
        var exact = await GetJsonAsync(api, "/api/products/by-sku/scan-me");
        Assert.Equal(productId, exact.GetProperty("id").GetInt32());
        using (var opened = await api.Client.PostAsJsonAsync("/api/shifts/open", new { openingCash = 100m }))
            await ApiProcess.EnsureSuccessAsync(opened);
        var key = Guid.NewGuid().ToString();
        async Task<JsonElement> CheckoutAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sales")
            {
                Content = JsonContent.Create(new
                {
                    paymentMethod = "Cash", cashTendered = 20m,
                    items = new[] { new { productId, quantity = 1, discountAmount = 0m } }
                })
            };
            request.Headers.Add("Idempotency-Key", key);
            using var response = await api.Client.SendAsync(request);
            await ApiProcess.EnsureSuccessAsync(response);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        var sale = await CheckoutAsync();
        var repeat = await CheckoutAsync();
        Assert.Equal(sale.GetProperty("id").GetInt32(), repeat.GetProperty("id").GetInt32());
        Assert.Equal(10m, repeat.GetProperty("changeDue").GetDecimal());
        var saleId = sale.GetProperty("id").GetInt32();
        var saleItemId = sale.GetProperty("items")[0].GetProperty("saleItemId").GetInt32();
        using (var returned = await api.Client.PostAsJsonAsync($"/api/sales/{saleId}/returns", new
        {
            refundMethod = "Cash", reason = "Browser/HTTP regression",
            items = new[] { new { saleItemId, quantity = 1 } }
        })) await ApiProcess.EnsureSuccessAsync(returned);
        using (var ended = await api.Client.PostAsJsonAsync("/api/shifts/end", new { }))
        {
            await ApiProcess.EnsureSuccessAsync(ended);
            var totals = await ended.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(100m, totals.GetProperty("expectedCash").GetDecimal());
        }
        using (var closed = await api.Client.PostAsJsonAsync("/api/shifts/close", new { closingCash = 100m }))
        {
            await ApiProcess.EnsureSuccessAsync(closed);
            var totals = await closed.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(0m, totals.GetProperty("cashVariance").GetDecimal());
        }
        await using var verify = database.Open();
        Assert.Single(await verify.Sales.ToListAsync());
        Assert.Equal(5, await verify.Products.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync());
    }

    [PgFact]
    public async Task Email_enabled_api_starts_and_sends_test_email_over_verified_tls()
    {
        await using var smtp = new SmtpCapture();
        await using var database = await PostgresDatabase.CreateAsync(migrate: false);
        await using var api = await ApiProcess.StartAsync(database, smtp.EnvironmentSettings);
        await api.LoginAsync();
        using (var settings = await api.Client.PutAsJsonAsync("/api/email-settings", new
        {
            managerEmail = "manager@example.test", alertsEnabled = true
        })) await ApiProcess.EnsureSuccessAsync(settings);
        using (var sent = await api.Client.PostAsJsonAsync("/api/email-settings/test", new { }))
            await ApiProcess.EnsureSuccessAsync(sent);
        var message = await smtp.NextMessageAsync();
        Assert.Equal("POS System SMTP test", message.Subject);
        Assert.Contains("manager@example.test", message.To.ToString());
        Assert.Contains("configuration is working", message.HtmlBody);
    }

    private static async Task<JsonElement> GetJsonAsync(ApiProcess api, string path)
    {
        using var response = await api.Client.GetAsync(path);
        await ApiProcess.EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
