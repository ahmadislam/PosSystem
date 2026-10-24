using Microsoft.Extensions.FileProviders;

namespace PosSystem.Api.Extensions;

/// <summary>Optional same-origin hosting for a single-container demo deployment.</summary>
public static class ClientHosting
{
    public static bool Configure(WebApplicationBuilder builder)
    {
        if (!builder.Configuration.GetValue<bool>("Web:ServeClient")) return false;
        var directory = builder.Configuration["Web:Directory"]
            ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
        if (!File.Exists(Path.Combine(directory, "index.html")))
            throw new InvalidOperationException("Build the Angular client before enabling Web:ServeClient.");
        builder.Configuration["Web:Directory"] = Path.GetFullPath(directory);

        var origin = builder.Configuration["Web:PublicOrigin"] ?? builder.Configuration["RENDER_EXTERNAL_URL"];
        if (!string.IsNullOrWhiteSpace(origin))
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https"
                || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/"
                || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("Web:PublicOrigin must be an HTTPS origin without a path or credentials.");
            var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
            builder.Configuration[$"Cors:Origins:{origins.Length}"] = uri.GetLeftPart(UriPartial.Authority);
        }
        else if (builder.Environment.IsProduction())
            throw new InvalidOperationException("Set Web:PublicOrigin (Render supplies RENDER_EXTERNAL_URL automatically).");
        return true;
    }

    public static void MapClientFallback(WebApplication app)
    {
        var directory = app.Configuration["Web:Directory"]!;
        app.MapFallback("{*path:nonfile}", async context =>
        {
            if ((!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
                || context.Request.Path.StartsWithSegments("/api")
                || context.Request.Path.StartsWithSegments("/health")
                || context.Request.Path.StartsWithSegments("/swagger"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-cache";
            if (!HttpMethods.IsHead(context.Request.Method))
                await context.Response.SendFileAsync(Path.Combine(directory, "index.html"));
        });
    }

    public static void UseClientFiles(WebApplication app)
    {
        var files = new PhysicalFileProvider(app.Configuration["Web:Directory"]!);
        app.Lifetime.ApplicationStopped.Register(files.Dispose);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
    }
}
