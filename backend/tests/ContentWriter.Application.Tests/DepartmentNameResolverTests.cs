using ContentWriter.Application.Providers;
using ContentWriter.Application.Services.Export;

namespace ContentWriter.Application.Tests;

public class DepartmentNameResolverTests
{
    [Fact]
    public void Resolve_returns_explicit_override_unmodified()
    {
        // Categories are an exact-match lookup against a live table (e.g. "Customer Service"),
        // so the override must pass through untouched — not lowercased/hyphenated.
        var result = DepartmentNameResolver.Resolve("Customer Service");
        Assert.Equal("Customer Service", result);
    }

    [Fact]
    public void Resolve_trims_whitespace()
    {
        var result = DepartmentNameResolver.Resolve("  Sales  ");
        Assert.Equal("Sales", result);
    }

    [Fact]
    public void Resolve_throws_when_no_override_provided()
    {
        Assert.Throws<ContentGenerationException>(() => DepartmentNameResolver.Resolve(null));
    }

    [Theory]
    [InlineData("Predictive Cash Flow Forecasting", "Predictive Cash Flow Forecasting")]
    [InlineData("  Smart Bank Reconciliation  ", "Smart Bank Reconciliation")]
    [InlineData("foo/bar:baz", "foobarbaz")]
    public void ResolveTopicFolder_uses_target_keyword(string keyword, string expected)
    {
        var result = DepartmentNameResolver.ResolveTopicFolder(keyword, "fallback-slug");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveTopicFolder_falls_back_to_slug_when_keyword_empty()
    {
        var result = DepartmentNameResolver.ResolveTopicFolder("   ", "smart-bank-reconciliation");
        Assert.Equal("smart bank reconciliation", result);
    }
}
