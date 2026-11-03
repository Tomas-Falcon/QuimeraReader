using FluentAssertions;
using Microsoft.AspNetCore.Http;
using QuimeraReader.API.Security;
using Xunit;

namespace QuimeraReader.Tests.Api;

public class ApiKeyAuthTests
{
    private const string Key = "super-secret-key";

    [Fact]
    public void WithoutConfiguredKey_EverythingIsAuthenticated()
    {
        var auth = new ApiKeyAuth(null);

        auth.IsEnabled.Should().BeFalse();
        auth.IsAuthenticated(new DefaultHttpContext()).Should().BeTrue();
    }

    [Fact]
    public void WithConfiguredKey_RequestWithoutCredentials_IsRejected()
    {
        var auth = new ApiKeyAuth(Key);

        auth.IsAuthenticated(new DefaultHttpContext()).Should().BeFalse();
    }

    [Fact]
    public void Header_WithCorrectKey_IsAccepted_AndWrongKeyRejected()
    {
        var auth = new ApiKeyAuth(Key);

        var ok = new DefaultHttpContext();
        ok.Request.Headers[ApiKeyAuth.HeaderName] = Key;
        var wrong = new DefaultHttpContext();
        wrong.Request.Headers[ApiKeyAuth.HeaderName] = "nope";

        auth.IsAuthenticated(ok).Should().BeTrue();
        auth.IsAuthenticated(wrong).Should().BeFalse();
    }

    [Fact]
    public void QueryToken_IsAccepted_ForMediaUrls()
    {
        var auth = new ApiKeyAuth(Key);
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString($"?{ApiKeyAuth.QueryName}={Key}");

        auth.IsAuthenticated(context).Should().BeTrue();
    }

    [Fact]
    public void Cookie_ContainsDerivedValue_NotTheRawKey()
    {
        var auth = new ApiKeyAuth(Key);
        var withCookie = new DefaultHttpContext();
        withCookie.Request.Headers.Cookie = $"{ApiKeyAuth.CookieName}={auth.CookieValue}";
        var withRawKeyCookie = new DefaultHttpContext();
        withRawKeyCookie.Request.Headers.Cookie = $"{ApiKeyAuth.CookieName}={Key}";

        auth.CookieValue.Should().NotContain(Key);
        auth.IsAuthenticated(withCookie).Should().BeTrue();
        auth.IsAuthenticated(withRawKeyCookie).Should().BeFalse();
    }
}
