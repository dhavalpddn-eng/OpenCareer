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
            "OpenCareerDangerButtonStyle",
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

        foreach (XElement dictionary in document.Root!
                     .DescendantsAndSelf()
                     .Where(static element => element.Name.LocalName == "ResourceDictionary"))
        {
            string[] duplicates = dictionary
                .Elements()
                .Select(element => element.Attribute(XamlNamespace + "Key")?.Value)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .GroupBy(static value => value, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key)
                .ToArray();

            Assert.True(
                duplicates.Length == 0,
                $"{fileName} repeats key(s) in one ResourceDictionary scope: {string.Join(", ", duplicates)}");
        }
    }

    [Fact]
    public void ButtonThemeResourcesCoverEveryReadableStateAndHighContrast()
    {
        XDocument document = XDocument.Load(GetDesignFilePath("DesignTokens.xaml"));
        XElement defaultTheme = FindThemeDictionary(document, "Default");
        XElement highContrastTheme = FindThemeDictionary(document, "HighContrast");

        foreach (string variant in new[] { "Primary", "Secondary", "Danger" })
        {
            Dictionary<string, string> backgrounds = new(StringComparer.Ordinal);
            foreach (string state in new[] { string.Empty, "PointerOver", "Pressed", "Disabled" })
            {
                string backgroundKey = $"OpenCareer{variant}ButtonBackground{state}";
                string foregroundKey = $"OpenCareer{variant}ButtonForeground{(state == "Disabled" ? "Disabled" : string.Empty)}";
                string borderKey = $"OpenCareer{variant}ButtonBorder{state}";

                string background = ReadSolidBrushColor(defaultTheme, backgroundKey);
                string foreground = ReadSolidBrushColor(defaultTheme, foregroundKey);
                backgrounds.Add(state, background);

                double contrast = ContrastRatio(ParseRgb(foreground), ParseRgb(background));
                string stateName = string.IsNullOrEmpty(state) ? "Normal" : state;
                Assert.True(
                    contrast >= 4.5,
                    $"{variant} {stateName} button contrast was {contrast:F2}:1.");

                AssertSystemHighContrastResource(highContrastTheme, backgroundKey);
                AssertSystemHighContrastResource(highContrastTheme, foregroundKey);
                AssertSystemHighContrastResource(highContrastTheme, borderKey);
            }

            Assert.NotEqual(backgrounds[string.Empty], backgrounds["PointerOver"]);
            Assert.NotEqual(backgrounds["PointerOver"], backgrounds["Pressed"]);
            Assert.NotEqual(backgrounds[string.Empty], backgrounds["Pressed"]);
        }
    }

    [Fact]
    public void ButtonStylesUseDeterministicTemplatesAndSystemFocus()
    {
        XDocument document = XDocument.Load(GetDesignFilePath("ComponentStyles.xaml"));
        XElement baseStyle = FindStyle(document, "OpenCareerButtonBaseStyle");

        Assert.Equal("{StaticResource DefaultButtonStyle}", baseStyle.Attribute("BasedOn")?.Value);
        AssertStyleSetter(baseStyle, "UseSystemFocusVisuals", "True");
        AssertStyleSetter(baseStyle, "MinHeight", "46");

        foreach (string variant in new[] { "Primary", "Secondary", "Danger" })
        {
            XElement style = FindStyle(document, $"OpenCareer{variant}ButtonStyle");
            AssertStyleSetter(
                style,
                "Template",
                $"{{StaticResource OpenCareer{variant}ButtonTemplate}}");
            AssertStyleSetter(
                style,
                "Background",
                $"{{ThemeResource OpenCareer{variant}ButtonBackground}}");
            AssertStyleSetter(
                style,
                "Foreground",
                $"{{ThemeResource OpenCareer{variant}ButtonForeground}}");

            XElement template = document
                .Descendants()
                .Single(element =>
                    element.Name.LocalName == "ControlTemplate" &&
                    string.Equals(
                        element.Attribute(XamlNamespace + "Key")?.Value,
                        $"OpenCareer{variant}ButtonTemplate",
                        StringComparison.Ordinal));

            string[] states = template
                .Descendants()
                .Where(static element => element.Name.LocalName == "VisualState")
                .Select(element => element.Attribute(XamlNamespace + "Name")?.Value)
                .Where(static name => name is not null)
                .Select(static name => name!)
                .ToArray();
            Assert.Contains("Normal", states);
            Assert.Contains("PointerOver", states);
            Assert.Contains("Pressed", states);
            Assert.Contains("Disabled", states);
            Assert.Contains(
                template.Descendants(),
                static element =>
                    element.Name.LocalName == "ContentPresenter" &&
                    string.Equals(element.Attribute(XamlNamespace + "Name")?.Value, "ButtonContent", StringComparison.Ordinal));
        }

        Assert.DoesNotContain(
            document.Descendants().Where(static element => element.Name.LocalName == "Setter"),
            static setter => setter.Attribute("Property")?.Value == "styles:ButtonStateResources.Variant");
    }

    [Fact]
    public void ProductionButtonsUseSharedVariantsWithoutLocalPaletteOverrides()
    {
        HashSet<string> allowedStyles =
        [
            "{StaticResource OpenCareerPrimaryButtonStyle}",
            "{StaticResource OpenCareerSecondaryButtonStyle}",
            "{StaticResource OpenCareerDangerButtonStyle}"
        ];
        string[] paths = GetProductionButtonXamlPaths().ToArray();
        Assert.Contains(paths, static path => Path.GetFileName(path) == "MainWindow.xaml");
        Assert.Contains(paths, static path => Path.GetFileName(path) == "CareerPage.xaml");
        Assert.Contains(paths, static path => Path.GetFileName(path) == "JobsPage.xaml");
        Assert.Contains(paths, static path => Path.GetFileName(path) == "CurrentFlightPage.xaml");

        int buttonCount = 0;

        foreach (string path in paths)
        {
            XDocument document = XDocument.Load(path);
            foreach (XElement button in document.Descendants().Where(static element => element.Name.LocalName == "Button"))
            {
                buttonCount++;
                string description = button.Attribute(XamlNamespace + "Name")?.Value
                    ?? button.Attribute("Content")?.Value
                    ?? "unnamed Button";
                string? style = button.Attribute("Style")?.Value;

                Assert.True(
                    style is not null && allowedStyles.Contains(style),
                    $"{Path.GetFileName(path)} {description} bypasses the shared Button variants.");

                foreach (string property in new[] { "Background", "Foreground", "BorderBrush", "RequestedTheme", "Template" })
                {
                    Assert.Null(button.Attribute(property));
                }

                Assert.DoesNotContain(
                    button.Elements(),
                    static element => element.Name.LocalName == "Button.Template");
            }
        }

        Assert.True(buttonCount >= 32, $"Expected the complete production Button audit; found {buttonCount} controls.");
    }

    [Fact]
    public void JobsPagePresentsFlightCenterWorkflowAndUnhiddenTestAction()
    {
        XDocument page = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "UiContracts", "JobsPage.xaml"));
        string[] visibleText = page
            .Descendants()
            .Select(element => element.Attribute("Text")?.Value ?? element.Attribute("Content")?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToArray();

        Assert.Contains("CAREER FLIGHT CENTER", visibleText);
        Assert.Contains("AIRCRAFT", visibleText);
        Assert.Contains("CONTRACT", visibleText);
        Assert.Contains("DISPATCH", visibleText);
        Assert.Contains("FLY", visibleText);
        Assert.Contains("CREATE TEST FLIGHT", visibleText);
        Assert.DoesNotContain(page.Descendants(), static element => element.Name.LocalName == "Expander");

        XElement testButton = page
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button" &&
                string.Equals(element.Attribute("Click")?.Value, "GenerateDevelopmentFlight_Click", StringComparison.Ordinal));
        Assert.Equal("{StaticResource OpenCareerPrimaryButtonStyle}", testButton.Attribute("Style")?.Value);
        Assert.Equal("Stretch", testButton.Attribute("HorizontalAlignment")?.Value);
    }

    [Fact]
    public void CareerStudentTrainingSurfaceCanStartButCannotCompleteOrGrant()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "UiButtons");
        XDocument page = XDocument.Load(Path.Combine(directory, "CareerPage.xaml"));
        string code = File.ReadAllText(Path.Combine(directory, "CareerPage.xaml.cs"));

        XElement start = page
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && string.Equals(
                    element.Attribute("Click")?.Value,
                    "StartStudentTraining_Click",
                    StringComparison.Ordinal));

        Assert.Equal(
            "{Binding CanStartStudentTraining}",
            start.Attribute("IsEnabled")?.Value);
        Assert.Contains("StartStudentTrainingAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CompleteAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AwardAsync", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ButtonDesignSystemDoesNotUsePointerHandlers()
    {
        foreach (string path in GetProductionButtonXamlPaths())
        {
            XDocument document = XDocument.Load(path);
            Assert.DoesNotContain(
                document.Descendants().Attributes(),
                static attribute =>
                    string.Equals(attribute.Name.LocalName, "PointerEntered", StringComparison.Ordinal) ||
                    string.Equals(attribute.Name.LocalName, "PointerExited", StringComparison.Ordinal));
        }

        foreach (string path in GetProductionButtonCodePaths())
        {
            string source = File.ReadAllText(path);
            Assert.DoesNotContain("PointerEntered", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PointerExited", source, StringComparison.Ordinal);
        }
    }

    private static void AssertStyleSetter(XElement style, string property, string expectedValue)
    {
        XElement setter = style
            .Elements()
            .Single(element =>
                element.Name.LocalName == "Setter" &&
                string.Equals(element.Attribute("Property")?.Value, property, StringComparison.Ordinal));

        Assert.Equal(expectedValue, setter.Attribute("Value")?.Value);
    }

    private static XElement FindStyle(XDocument document, string styleKey) =>
        document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style" &&
                string.Equals(element.Attribute(XamlNamespace + "Key")?.Value, styleKey, StringComparison.Ordinal));

    private static XElement FindThemeDictionary(XDocument document, string theme) =>
        document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ResourceDictionary" &&
                string.Equals(element.Attribute(XamlNamespace + "Key")?.Value, theme, StringComparison.Ordinal));

    private static string ReadSolidBrushColor(XElement dictionary, string resourceKey) =>
        dictionary
            .Elements()
            .Single(element =>
                element.Name.LocalName == "SolidColorBrush" &&
                string.Equals(element.Attribute(XamlNamespace + "Key")?.Value, resourceKey, StringComparison.Ordinal))
            .Attribute("Color")?.Value
        ?? throw new InvalidDataException($"Button resource '{resourceKey}' must declare a color.");

    private static void AssertSystemHighContrastResource(XElement dictionary, string resourceKey)
    {
        XElement resource = dictionary
            .Elements()
            .Single(element =>
                string.Equals(element.Attribute(XamlNamespace + "Key")?.Value, resourceKey, StringComparison.Ordinal));
        string? systemKey = resource.Attribute("ResourceKey")?.Value;
        Assert.True(
            systemKey is not null && systemKey.StartsWith("System", StringComparison.Ordinal),
            $"{resourceKey} must defer to a system high-contrast resource.");
    }

    private static IEnumerable<string> GetProductionButtonXamlPaths()
    {
        string buttonDirectory = Path.Combine(AppContext.BaseDirectory, "UiButtons");
        IEnumerable<string> buttonFiles = Directory.Exists(buttonDirectory)
            ? Directory.EnumerateFiles(buttonDirectory, "*.xaml", SearchOption.TopDirectoryOnly)
            : [];

        return buttonFiles
            .Concat(
            [
                Path.Combine(AppContext.BaseDirectory, "UiContracts", "JobsPage.xaml"),
                Path.Combine(AppContext.BaseDirectory, "UiContracts", "CurrentFlightPage.xaml")
            ])
            .Where(File.Exists)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
    }

    private static IEnumerable<string> GetProductionButtonCodePaths()
    {
        string buttonDirectory = Path.Combine(AppContext.BaseDirectory, "UiButtons");
        IEnumerable<string> buttonFiles = Directory.Exists(buttonDirectory)
            ? Directory.EnumerateFiles(buttonDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            : [];

        return buttonFiles
            .Concat(
            [
                Path.Combine(AppContext.BaseDirectory, "UiContracts", "JobsPage.xaml.cs"),
                Path.Combine(AppContext.BaseDirectory, "UiContracts", "CurrentFlightPage.xaml.cs")
            ])
            .Where(File.Exists)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
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
