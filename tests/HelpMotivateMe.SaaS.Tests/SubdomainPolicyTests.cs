using HelpMotivateMe.SaaS.Api.Services;

namespace HelpMotivateMe.SaaS.Tests;

public sealed class SubdomainPolicyTests
{
    [Theory]
    [InlineData("demo", "demo")]
    [InlineData(" Acme-42 ", "acme-42")]
    [InlineData("a", "a")]
    public void TryNormalize_AcceptsTenantNames(string value, string expected)
    {
        var valid = SubdomainPolicy.TryNormalize(value, out var normalized);

        Assert.True(valid);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-demo")]
    [InlineData("demo-")]
    [InlineData("two.words")]
    [InlineData("API")]
    [InlineData("saas")]
    public void TryNormalize_RejectsInvalidOrReservedNames(string? value)
    {
        Assert.False(SubdomainPolicy.TryNormalize(value, out _));
    }
}