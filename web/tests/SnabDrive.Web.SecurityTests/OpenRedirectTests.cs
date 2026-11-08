using SnabDrive.Web.Services;
using Xunit;

namespace SnabDrive.Web.SecurityTests;

/// <summary>
/// Открытое перенаправление (open redirect) на странице входа: параметр returnUrl
/// приходит из URL, и если принимать любой адрес, ссылку вида
/// /login?returnUrl=https://evil.example можно использовать для фишинга.
/// </summary>
public sealed class OpenRedirectTests
{
    [Theory]
    [InlineData("/registry")]
    [InlineData("/")]
    [InlineData("/workflow?stage=5")]
    [InlineData("/контур-избранное")]
    public void Local_relative_url_is_accepted(string returnUrl)
        => Assert.Equal(returnUrl, AccountEndpoints.ToSafeLocalUrl(returnUrl));

    [Theory]
    [InlineData("https://evil.example/phish")]
    [InlineData("http://evil.example")]
    [InlineData("//evil.example")]            // протоколо-относительный адрес
    [InlineData("/\\evil.example")]           // обход через обратный слэш
    [InlineData("///evil.example")]
    [InlineData("https:evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("  ")]
    [InlineData("")]
    public void Anything_else_falls_back_to_root(string returnUrl)
        => Assert.Equal("/", AccountEndpoints.ToSafeLocalUrl(returnUrl));

    [Fact]
    public void Null_return_url_falls_back_to_root()
        => Assert.Equal("/", AccountEndpoints.ToSafeLocalUrl(null));
}
