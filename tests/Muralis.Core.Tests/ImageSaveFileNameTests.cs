using Muralis.Services;

namespace Muralis.Core.Tests;

/// <summary>Issue #48 : avec Bing, le nom d'enregistrement valait « th » (sans extension)
/// et bloquait tout enregistrement suivant (« déjà enregistrée »).</summary>
public class ImageSaveFileNameTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 9, 30, 15);
    private const string CachePath = @"C:\cache\webcache\src\0123456789ABCDEF.jpg";

    private static string Name(string? url) =>
        ImageSaveService.BuildFileName(url is null ? null : new Uri(url), CachePath, Now);

    [Fact]
    public void Bing_UsesTheIdParameter()
    {
        // Format réel renvoyé par l'API (relatif, résolu contre l'URL de requête).
        var url = new Uri(new Uri("https://www.bing.com/HPImageArchive.aspx"),
            "/th?id=OHR.TrailCedars_FR-FR7583904016_1920x1080.jpg&rf=LaDigue_1920x1080.jpg&pid=hp");

        Assert.Equal("OHR.TrailCedars_FR-FR7583904016_1920x1080.jpg",
            ImageSaveService.BuildFileName(url, CachePath, Now));
    }

    [Fact]
    public void TwoBingDays_GetDistinctNames()
    {
        Assert.NotEqual(
            Name("https://www.bing.com/th?id=OHR.DayOne_FR-FR1_1920x1080.jpg&pid=hp"),
            Name("https://www.bing.com/th?id=OHR.DayTwo_FR-FR2_1920x1080.jpg&pid=hp"));
    }

    [Fact]
    public void Wallhaven_KeepsOriginalName()
    {
        Assert.Equal("wallhaven-94x38z.jpg", Name("https://w.wallhaven.cc/full/94/wallhaven-94x38z.jpg"));
    }

    [Theory]
    [InlineData("https://api.example.com/get.php?id=42")]   // extension non-image : jamais « get.php »
    [InlineData("https://api.example.com/image")]           // pas d'extension
    [InlineData("https://api.example.com/")]                 // pas de nom
    public void NonImageLeaf_FallsBackToTimestamp(string url)
    {
        Assert.Equal("Muralis-20261117-093015.jpg", Name(url));
    }

    [Fact]
    public void UnknownUrl_FallsBackToTimestampWithCacheExtension()
    {
        Assert.Equal("Muralis-20261117-093015.jpg", Name(null));
    }

    [Fact]
    public void InvalidCharacters_AreReplaced()
    {
        Assert.Equal("a_b.png", Name("https://x/dl?id=a%3Ab.png"));
    }

    [Fact]
    public void IdParameter_CannotTraversePaths()
    {
        // Le paramètre id vient du réseau : « ../ » encodé ne doit jamais produire un séparateur.
        string name = Name("https://x/dl?id=..%2F..%2Fevil.png");

        Assert.Equal(".._.._evil.png", name);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
    }
}
