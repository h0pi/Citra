namespace Citra.Api;

/// <summary>Security headers on every response, including errors and /widget.js.</summary>
internal static class SecurityHeaders
{
    /// <summary>
    /// The API only returns data (JSON, files, the widget script), never pages: a response opened in a browser runs
    /// nothing and cannot be framed by another site. Websites load /widget.js with a script tag, which this policy
    /// does not affect.
    /// </summary>
    public const string ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use((context, next) =>
    {
        // Set when the response starts, so headers cleared by the exception handler are added back.
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff"; // a file is only ever treated as its declared type
            headers["Referrer-Policy"] = "no-referrer";
            // The development API reference (/scalar) is a page with its own scripts.
            if (!context.Request.Path.StartsWithSegments("/scalar"))
            {
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
            }

            return Task.CompletedTask;
        });
        return next(context);
    });
}
