using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SnabDrive.Web.Services.Security;
using Xunit;

namespace SnabDrive.Web.SecurityTests;

/// <summary>
/// Заголовки безопасности и связанные с ними настройки: CSP, запрет встраивания
/// в iframe, отключение угадывания типа содержимого, политика cookie, CORS.
/// </summary>
public sealed class SecurityHeadersTests
{
    private static SecurityHeadersMiddleware Create(SecurityOptions options) =>
        new(_ => Task.CompletedTask, Options.Create(options));

    [Fact]
    public async Task Adds_security_headers_to_every_response()
    {
        var context = new DefaultHttpContext();

        await Create(new SecurityOptions()).InvokeAsync(context);

        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
        Assert.Equal("strict-origin-when-cross-origin", context.Response.Headers["Referrer-Policy"].ToString());
        Assert.Equal("same-origin", context.Response.Headers["Cross-Origin-Opener-Policy"].ToString());
        Assert.NotEmpty(context.Response.Headers["Permissions-Policy"].ToString());
    }

    [Fact]
    public async Task Csp_allows_only_own_scripts_and_forbids_framing()
    {
        var context = new DefaultHttpContext();

        await Create(new SecurityOptions()).InvokeAsync(context);

        var csp = context.Response.Headers["Content-Security-Policy"].ToString();
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.DoesNotContain("*", csp);
    }

    [Fact]
    public async Task Headers_can_be_switched_off_from_configuration()
    {
        var context = new DefaultHttpContext();

        await Create(new SecurityOptions { EnableSecurityHeaders = false }).InvokeAsync(context);

        Assert.False(context.Response.Headers.ContainsKey("Content-Security-Policy"));
        Assert.False(context.Response.Headers.ContainsKey("X-Frame-Options"));
    }

    [Fact]
    public void Upgrade_insecure_requests_is_opt_in()
    {
        Assert.DoesNotContain("upgrade-insecure-requests", new SecurityOptions().BuildContentSecurityPolicy());
        Assert.Contains("upgrade-insecure-requests",
            new SecurityOptions { UpgradeInsecureRequests = true }.BuildContentSecurityPolicy());
    }

    [Fact]
    public void Empty_policy_means_no_csp_header()
        => Assert.Equal(string.Empty, new SecurityOptions { ContentSecurityPolicy = "  " }.BuildContentSecurityPolicy());

    [Fact]
    public void Cross_origin_access_is_closed_by_default()
        => Assert.Empty(new SecurityOptions().AllowedOrigins);

    [Fact]
    public void Cookie_secure_policy_is_read_from_configuration()
    {
        Assert.Equal(CookieSecurePolicyValue.SameAsRequest, new SecurityOptions().ResolveCookieSecurePolicy());
        Assert.Equal(CookieSecurePolicyValue.Always,
            new SecurityOptions { CookieSecurePolicy = "Always" }.ResolveCookieSecurePolicy());

        // Опечатка в конфиге не должна ни ронять сайт, ни ослаблять настройку незаметно.
        Assert.Equal(CookieSecurePolicyValue.SameAsRequest,
            new SecurityOptions { CookieSecurePolicy = "Alwyas" }.ResolveCookieSecurePolicy());
    }

    [Fact]
    public void Rate_limits_have_safe_defaults()
    {
        var options = new SecurityOptions();

        Assert.Equal(1200, options.GlobalRequestsPerMinute);
        Assert.Equal(600, options.ApiRequestsPerMinute);
        Assert.Equal(10, options.LoginAttemptsPerMinute);
    }
}
