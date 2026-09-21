using System.Globalization;
using System.Xml.Linq;

namespace OpenCareer.Tests;

public sealed class UiDesignSystemContractTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void CanonicalPaletteKeepsVividPaperAndCoolShell()
    {
        IReadOnlyDictionary<string, string> colors = ReadNamedColors();

        Assert.Equal("#FFFDF8", colors["OpenCareerPaperColor"]);
        Assert.Equal("#F5F2E9", colors["OpenCareerPaperAltColor"]);
        Assert.Equal("#111C26", colors["OpenCareerShellBackgroundColor"]);
        Assert.Equal("#29475C", colors["OpenCareerNavyColor"]);

        Rgb paper = ParseRgb(colors["OpenCareerPaperColor"]);
        Assert.True(
            RelativeLuminance(paper) >= 0.95,
            "Primary paper must remain a vivid near-white surface.");
    }

    [Fact]
    public void CanonicalTextPairsMeetNormalTextContrast()
    {
        IReadOnlyDictionary<string, string> colors = ReadNamedColors();

        Assert.True(
            ContrastRatio(
                ParseRgb(colors["OpenCareerShellTextColor"]),
                ParseRgb(colors["OpenCareerShellBackgroundColor"])) >= 4.5,
            "Shell text must maintain at least 4.5:1 contrast.");

        Assert.True(
            ContrastRatio(
                ParseRgb(colors["OpenCareerInkColor"]),
                ParseRgb(colors["OpenCareerPaperColor"])) >= 4.5,
            "Paper ink must maintain at least 4.5:1 contrast.");

        Assert.True(
            ContrastRatio(
                ParseRgb(colors["OpenCareerInkMutedColor"]),
                ParseRgb(colors["OpenCareerPaperColor"])) >= 4.5,
            "Muted paper text must maintain at least 4.5:1 contrast.");
    }

    [Fact]
    public void ReusableComponentLibraryContainsRequiredProductionStyles()
    {
        XDocument document = XDocument.Load(GetDesignFilePath("ComponentStyles.xaml"));
        HashSet<string> keys = document
            .Descendants()
            .Select(element => element.Attribute(XamlNamespace + "Key")?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToHashSet(StringComparer.Ordinal);

        string[] required =
        [
            "OpenCareerPageTitleStyle",
            "OpenCareerPaperCardStyle",
            "OpenCareerMetalCardStyle",
            "OpenCareerPrimaryButtonStyle",
            "OpenCareerSecondaryButtonStyle",
            "OpenCareerTextBoxStyle",
            "OpenCareerComboBoxStyle",
            "OpenCareerCheckBoxStyle",
            "OpenCareerToggleSwitchStyle",
            "OpenCareerTabStyle",
            "OpenCareerListViewStyle",
            "OpenCareerListViewItemStyle",
            "OpenCareerTableHeaderRowStyle",
            "OpenCareerTableRowStyle",
            "OpenCareerTableSelectedRowStyle",
            "OpenCareerSuccessBadgeStyle",
            "OpenCareerWarningBadgeStyle",
            "OpenCareerDangerBadgeStyle",
            "OpenCareerUnavailableBadgeStyle"
        ];

        foreach (string key in required)
            Assert.Contains(key, keys);
    }

    [Theory]
    [InlineData("DesignTokens.xaml")]
    [InlineData("ComponentStyles.xaml")]
    public void ResourceDictionariesDoNotContainDuplicateKeys(string fileName)
    {
        XDocument document = XDocument.Load(GetDesignFilePath(fileName));

        string[] duplicates = document
            .Descendants()
            .Select(element => element.Attribute(XamlNamespace + "Key")?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .GroupBy(static value => value, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    private static IReadOnlyDictionary<string, string> ReadNamedColors()
    {
        XDocument document = XDocument.Load(GetDesignFilePath("DesignTokens.xaml"));

        return document
            .Descendants()
            .Where(static element => element.Name.LocalName == "Color")
            .Select(element => new
            {
                Key = element.Attribute(XamlNamespace + "Key")?.Value,
                Value = element.Value.Trim()
            })
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(
                static pair => pair.Key!,
                static pair => pair.Value,
                StringComparer.Ordinal);
    }

    private static string GetDesignFilePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "UiDesign", fileName);

    private static Rgb ParseRgb(string value)
    {
        string hex = value.TrimStart('#');
        if (hex.Length != 6)
            throw new InvalidDataException($"Expected #RRGGBB color, got '{value}'.");

        return new Rgb(
            byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private static double ContrastRatio(Rgb first, Rgb second)
    {
        double firstLuminance = RelativeLuminance(first);
        double secondLuminance = RelativeLuminance(second);
        double lighter = Math.Max(firstLuminance, secondLuminance);
        double darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Rgb color) =>
        0.2126 * Linearize(color.R / 255.0) +
        0.7152 * Linearize(color.G / 255.0) +
        0.0722 * Linearize(color.B / 255.0);

    private static double Linearize(double component) =>
        component <= 0.04045
            ? component / 12.92
            : Math.Pow((component + 0.055) / 1.055, 2.4);

    private readonly record struct Rgb(byte R, byte G, byte B);
}
