using FHT.Access.Infrastructure.Http;

namespace FHT.Access.Tests;

public class GestaoUrlTests
{
    [Theory]
    [InlineData("http://localhost:4010", "http://localhost:4010/")]
    [InlineData("http://localhost:4010/", "http://localhost:4010/")]
    [InlineData("http://localhost:4010/api/v1", "http://localhost:4010/")]
    [InlineData("localhost:4010", "http://localhost:4010/")]
    [InlineData("https://api.example.com/api/v1/", "https://api.example.com/")]
    public void ResolveBaseAddress_Normalizes(string input, string expected)
    {
        Assert.Equal(expected, GestaoUrl.ResolveBaseAddress(input).ToString());
    }

    [Fact]
    public void ResolveBaseAddress_Empty_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => GestaoUrl.ResolveBaseAddress("  "));
        Assert.Contains("Base URL", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://localhost:4010", "api/v1/access/device-auth", "http://localhost:4010/api/v1/access/device-auth")]
    [InlineData("http://localhost:4010/", "api/v1/access/device-auth", "http://localhost:4010/api/v1/access/device-auth")]
    public void ResolveBaseAddress_CombinesRelativePath(string input, string relative, string expected)
    {
        var absolute = new Uri(GestaoUrl.ResolveBaseAddress(input), relative);
        Assert.Equal(expected, absolute.ToString());
    }
}
