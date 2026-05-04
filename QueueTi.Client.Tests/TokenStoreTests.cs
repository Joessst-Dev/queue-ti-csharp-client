using System.Text;
using System.Text.Json;

namespace QueueTi.Client.Tests;

public sealed class TokenStoreTests
{
    private static string BuildJwt(long expUnixSeconds)
    {
        var header = Base64UrlEncode("""{"alg":"HS256","typ":"JWT"}""");
        var payload = Base64UrlEncode($$$"""{"sub":"test","exp":{{{expUnixSeconds}}}  }""");
        return $"{header}.{payload}.fakesignature";
    }

    private static string Base64UrlEncode(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    [Fact]
    public void Get_GivenInitialToken_ShouldReturnThatToken()
    {
        // Arrange (Given)
        var token = BuildJwt(DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        var sut = new TokenStore(token);

        // Act (When)
        var result = sut.Get();

        // Assert (Then)
        Assert.Equal(token, result);
    }

    [Fact]
    public void Set_GivenNewToken_ShouldReturnUpdatedToken()
    {
        // Arrange (Given)
        var initial = BuildJwt(DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        var updated = BuildJwt(DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds());
        var sut = new TokenStore(initial);

        // Act (When)
        sut.Set(updated);
        var result = sut.Get();

        // Assert (Then)
        Assert.Equal(updated, result);
    }

    [Fact]
    public async Task Set_GivenConcurrentWriters_ShouldNotThrow()
    {
        // Arrange (Given)
        var initial = BuildJwt(DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        var sut = new TokenStore(initial);

        // Act (When)
        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(() =>
        {
            var t = BuildJwt(DateTimeOffset.UtcNow.AddSeconds(i).ToUnixTimeSeconds());
            sut.Set(t);
            _ = sut.Get();
        }));

        var exception = await Record.ExceptionAsync(() => Task.WhenAll(tasks));

        // Assert (Then)
        Assert.Null(exception);
    }

    [Fact]
    public void GetExpiry_GivenWellFormedJwt_ShouldReturnCorrectExpiry()
    {
        // Arrange (Given)
        var expectedExpiry = DateTimeOffset.UtcNow.AddHours(1);
        var token = BuildJwt(expectedExpiry.ToUnixTimeSeconds());
        var sut = new TokenStore(token);

        // Act (When)
        var expiry = sut.GetExpiry();

        // Assert (Then)
        Assert.Equal(expectedExpiry.ToUnixTimeSeconds(), expiry.ToUnixTimeSeconds());
    }

    [Fact]
    public void GetExpiry_GivenTokenWithTwoSegments_ShouldThrowFormatException()
    {
        // Arrange (Given)
        var token = "only.twosegments";
        var sut = new TokenStore(token);

        // Act (When) / Assert (Then)
        Assert.Throws<FormatException>(() => sut.GetExpiry());
    }

    [Fact]
    public void GetExpiry_GivenPayloadWithNoExpClaim_ShouldThrowFormatException()
    {
        // Arrange (Given)
        var header = Base64UrlEncode("""{"alg":"HS256"}""");
        var payload = Base64UrlEncode("""{"sub":"no-exp-here"}""");
        var token = $"{header}.{payload}.sig";
        var sut = new TokenStore(token);

        // Act (When) / Assert (Then)
        Assert.Throws<FormatException>(() => sut.GetExpiry());
    }

    [Fact]
    public void GetExpiry_GivenInvalidBase64Payload_ShouldThrowFormatException()
    {
        // Arrange (Given)
        var token = "validheader.!!!notbase64!!.signature";
        var sut = new TokenStore(token);

        // Act (When) / Assert (Then)
        Assert.Throws<FormatException>(() => sut.GetExpiry());
    }
}
