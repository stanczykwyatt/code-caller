using Copy2FA.Core;

namespace Copy2FA.Core.Tests;

public class SenderNames
{
    [Fact]
    public void UsesTheDisplayName()
    {
        Assert.Equal("GitHub", SenderName.Display("GitHub <noreply@github.com>"));
        Assert.Equal("Apple", SenderName.Display("\"Apple\" <appleid@id.apple.com>"));
    }

    [Fact]
    public void FallsBackToTheDomain()
    {
        Assert.Equal("google.com", SenderName.Display("<no-reply@accounts.google.com>"));
        Assert.Equal("github.com", SenderName.Display("noreply@github.com"));
    }

    [Fact]
    public void KeepsCountryDomainsWhole()
    {
        Assert.Equal("example.co.uk", SenderName.Display("alerts@mail.example.co.uk"));
    }

    [Fact]
    public void KeepsPlainNames()
    {
        Assert.Equal("Microsoft account team", SenderName.Display("  Microsoft account team "));
    }
}
