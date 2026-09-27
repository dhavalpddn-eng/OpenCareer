using System.Globalization;
using System.Xml.Linq;

namespace OpenCareer.Tests;

public sealed class UiDesignSystemContractTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] ButtonStateSuffixes =
    [
        string.Empty,
        "PointerOver",
        "Pressed",
        "Disabled"
    ];

    private static readonly string[] NativeButtonResourcePrefixes =
    [
        "ButtonBackground",
        "ButtonForeground",
        "ButtonBorderBrush"
    ];

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
            "OpenCareerPaperTitleStyle",
            "OpenCareerPaperBodyStyle",
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
    [InlineData("PrimaryButtonStates.xaml")]
    [InlineData("SecondaryButtonStates.xaml")]
    [InlineData("DangerButtonStates.xaml")]
    [InlineData("ButtonStateRuntimeProbePage.xaml")]
    public void ResourceDictionariesDoNotContainDuplicateKeys(string fileName)
    {
        XDocument document = XDocument.Load(GetDesignFilePath(fileName));

        foreach (XElement dictionary in document.Root!
                     .DescendantsAndSelf()
                     .Where(static element =>
                         element.Name.LocalName == "ResourceDictionary" ||
                         element.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal)))
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

    [Theory]
    [InlineData("PrimaryButtonStates.xaml")]
    [InlineData("SecondaryButtonStates.xaml")]
    [InlineData("DangerButtonStates.xaml")]
    public void ButtonVariantStatesRemainDistinctAndReadable(string fileName)
    {
        IReadOnlyDictionary<string, string> brushColors = ReadNamedBrushColors();

        foreach (string theme in new[] { "Default", "Light" })
        {
            IReadOnlyDictionary<string, string> resources = ReadThemeResources(fileName, theme);
            Dictionary<string, string> backgrounds = new(StringComparer.Ordinal);

            foreach (string suffix in ButtonStateSuffixes)
            {
                string backgroundKey = $"ButtonBackground{suffix}";
                string foregroundKey = $"ButtonForeground{suffix}";
                string borderKey = $"ButtonBorderBrush{suffix}";

                Assert.Contains(backgroundKey, resources.Keys);
                Assert.Contains(foregroundKey, resources.Keys);
                Assert.Contains(borderKey, resources.Keys);

                string background = brushColors[resources[backgroundKey]];
                string foreground = brushColors[resources[foregroundKey]];
                backgrounds.Add(suffix, background);

                double contrast = ContrastRatio(ParseRgb(foreground), ParseRgb(background));
                string stateName = string.IsNullOrEmpty(suffix) ? "Normal" : suffix;
                Assert.True(
                    contrast >= 4.5,
                    $"{fileName} {theme} {stateName} contrast was {contrast:F2}:1.");
            }

            Assert.NotEqual(backgrounds[string.Empty], backgrounds["PointerOver"]);
            Assert.NotEqual(backgrounds["PointerOver"], backgrounds["Pressed"]);
            Assert.NotEqual(backgrounds[string.Empty], backgrounds["Pressed"]);
        }
    }

    [Theory]
    [InlineData("PrimaryButtonStates.xaml")]
    [InlineData("SecondaryButtonStates.xaml")]
    [InlineData("DangerButtonStates.xaml")]
    public void ButtonVariantStatesDeferToSystemHighContrastResources(string fileName)
    {
        IReadOnlyDictionary<string, string> resources = ReadThemeResources(fileName, "HighContrast");

        foreach (string suffix in ButtonStateSuffixes)
        {
            foreach (string prefix in NativeButtonResourcePrefixes)
            {
                string key = $"{prefix}{suffix}";
                Assert.True(resources.TryGetValue(key, out string? resourceKey), $"{fileName} lacks {key}.");
                Assert.True(
                    resourceKey is not null && resourceKey.StartsWith("System", StringComparison.Ordinal),
                    $"{fileName} {key} must defer to a system high-contrast resource.");
            }
        }
    }

    [Fact]
    public void ButtonStylesInstallVariantLocalResourcesAndKeepTheStockTemplate()
    {
        XDocument document = XDocument.Load(GetDesignFilePath("ComponentStyles.xaml"));

        XElement baseStyle = FindStyle(document, "OpenCareerButtonBaseStyle");
        Assert.Equal("{StaticResource DefaultButtonStyle}", baseStyle.Attribute("BasedOn")?.Value);

        AssertVariantSetter(document, "OpenCareerPrimaryButtonStyle", "Primary");
        AssertVariantSetter(document, "OpenCareerSecondaryButtonStyle", "Secondary");
        AssertVariantSetter(document, "OpenCareerDangerButtonStyle", "Danger");

        Assert.DoesNotContain(
            document.Descendants(),
            static element => element.Name.LocalName == "ControlTemplate");
        Assert.DoesNotContain(
            document.Descendants().Where(static element => element.Name.LocalName == "Setter"),
            static setter => string.Equals(setter.Attribute("Property")?.Value, "Template", StringComparison.Ordinal));
        Assert.DoesNotContain(
            document.Descendants().Where(static element => element.Name.LocalName == "Setter"),
            static setter => string.Equals(
                setter.Attribute("Property")?.Value,
                "UseSystemFocusVisuals",
                StringComparison.Ordinal));

        foreach (string fileName in new[]
                 {
                     "PrimaryButtonStates.xaml",
                     "SecondaryButtonStates.xaml",
                     "DangerButtonStates.xaml"
                 })
        {
            XDocument stateDocument = XDocument.Load(GetDesignFilePath(fileName));
            Assert.DoesNotContain(
                stateDocument.Descendants(),
                static element =>
                    element.Attribute(XamlNamespace + "Key")?.Value.StartsWith(
                        "FocusVisual",
                        StringComparison.Ordinal) == true);
        }

        string resourceScopeSource = File.ReadAllText(GetDesignFilePath("ButtonStateResources.cs"));
        Assert.Contains("button.Resources.MergedDictionaries.Add", resourceScopeSource, StringComparison.Ordinal);
        Assert.Contains("ms-appx:///Styles/{variant}ButtonStates.xaml", resourceScopeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Current.Resources", resourceScopeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeButtonStateKeysAreNeverDefinedAtApplicationOrPageScope()
    {
        IEnumerable<string> globallyScopedFiles =
        [
            GetDesignFilePath("App.xaml"),
            GetDesignFilePath("DesignTokens.xaml"),
            GetDesignFilePath("ComponentStyles.xaml"),
            GetDesignFilePath("ButtonStateRuntimeProbePage.xaml"),
            .. GetProductionButtonXamlPaths()
        ];

        foreach (string path in globallyScopedFiles)
        {
            XDocument document = XDocument.Load(path);
            string[] nativeKeys = document
                .Descendants()
                .Select(element => element.Attribute(XamlNamespace + "Key")?.Value)
                .Where(static value => value is not null && IsNativeButtonResourceKey(value))
                .Select(static value => value!)
                .ToArray();

            Assert.True(
                nativeKeys.Length == 0,
                $"{Path.GetFileName(path)} globally scopes native Button resource(s): {string.Join(", ", nativeKeys)}");
        }
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

    [Fact]
    public void RuntimeProbeUsesCompiledAppResourcesAndStockButtonTemplate()
    {
        string probeSource =
            File.ReadAllText(GetDesignFilePath("ButtonStateRuntimeProbe.cs"));
        string probeXaml =
            File.ReadAllText(GetDesignFilePath("ButtonStateRuntimeProbePage.xaml"));
        string appSource =
            File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory,
                "UiContracts",
                "App.xaml.cs"));
        string script =
            File.ReadAllText(GetDesignFilePath(
                "run-button-state-runtime-certification.ps1"));
        string workflow =
            File.ReadAllText(GetDesignFilePath("winui-build.yml"));
        XDocument probeDocument = XDocument.Parse(probeXaml);

        Assert.Equal(
            "OpenCareer.App.Diagnostics.ButtonStateRuntimeProbePage",
            probeDocument.Root?.Attribute(XamlNamespace + "Class")?.Value);

        IReadOnlyDictionary<string, string> probeButtonStyles = probeDocument
            .Descendants()
            .Where(static element => element.Name.LocalName == "Button")
            .Where(element => element.Attribute(XamlNamespace + "Name") is not null)
            .ToDictionary(
                element => element.Attribute(XamlNamespace + "Name")!.Value,
                element => element.Attribute("Style")?.Value
                    ?? throw new InvalidDataException("Probe Button must use a compiled style."),
                StringComparer.Ordinal);
        Assert.Equal(
            "{StaticResource OpenCareerPrimaryButtonStyle}",
            probeButtonStyles["PrimaryProbeButton"]);
        Assert.Equal(
            "{StaticResource OpenCareerSecondaryButtonStyle}",
            probeButtonStyles["SecondaryProbeButton"]);
        Assert.Equal(
            "{StaticResource OpenCareerDangerButtonStyle}",
            probeButtonStyles["DangerProbeButton"]);
        Assert.Equal(
            "{StaticResource DefaultButtonStyle}",
            probeButtonStyles["StockLightProbeButton"]);
        Assert.Equal(
            "{StaticResource DefaultButtonStyle}",
            probeButtonStyles["StockDarkProbeButton"]);

        Dictionary<string, string> probeAliases = probeDocument
            .Descendants()
            .Where(static element => element.Name.LocalName == "StaticResource")
            .ToDictionary(
                element => element.Attribute(XamlNamespace + "Key")?.Value
                    ?? throw new InvalidDataException("Probe resource alias lacks x:Key."),
                element => element.Attribute("ResourceKey")?.Value
                    ?? throw new InvalidDataException("Probe resource alias lacks ResourceKey."),
                StringComparer.Ordinal);
        Assert.NotEmpty(probeAliases);
        Assert.All(
            probeAliases,
            pair => Assert.Equal($"Probe{pair.Value}", pair.Key));
        Assert.Equal(probeAliases.Count, probeAliases.Keys.Distinct(StringComparer.Ordinal).Count());
        string[] expectedProbeTokens = probeSource
            .Split('"', StringSplitOptions.RemoveEmptyEntries)
            .Where(static value =>
                value.StartsWith("OpenCareer", StringComparison.Ordinal) &&
                value.EndsWith("Brush", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            expectedProbeTokens,
            probeAliases.Values
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());

        XElement[] stockProbeButtons = probeDocument
            .Descendants()
            .Where(element =>
                element.Attribute(XamlNamespace + "Name")?.Value.StartsWith(
                    "Stock",
                    StringComparison.Ordinal) == true)
            .ToArray();
        Assert.Equal(2, stockProbeButtons.Length);
        Assert.All(stockProbeButtons, stockProbeButton =>
        {
            Assert.Equal("False", stockProbeButton.Attribute("IsTabStop")?.Value);
            Assert.Equal(
                "Raw",
                stockProbeButton.Attributes().Single(attribute =>
                    attribute.Name.LocalName == "AutomationProperties.AccessibilityView").Value);
        });

        Assert.Contains("InitializeComponent", probeSource, StringComparison.Ordinal);
        Assert.Contains("OpenCareerShellBackgroundBrush", probeXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenCareerShellBrush", probeXaml, StringComparison.Ordinal);
        Assert.Equal(
            "OpenCareerNavyBrush",
            probeAliases["ProbeOpenCareerNavyBrush"]);
        Assert.Contains("page.GetExpectedBrush", probeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("XamlApplication.Current.Resources", probeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetApplicationResource", probeSource, StringComparison.Ordinal);
        Assert.Contains("VisualStateManager.GoToState", probeSource, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(TimeSpan.FromMilliseconds(100))", probeSource, StringComparison.Ordinal);
        Assert.Contains("FindStockContentPresenter", probeSource, StringComparison.Ordinal);
        Assert.Contains("UseSystemFocusVisuals", probeSource, StringComparison.Ordinal);
        Assert.Contains("ButtonAutomationPeer", probeSource, StringComparison.Ordinal);
        Assert.Contains("ButtonStateResources.SetVariant", probeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new ControlTemplate", probeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlTemplate", probeXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PointerEntered", probeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PointerExited", probeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PointerEntered", probeXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PointerExited", probeXaml, StringComparison.Ordinal);
        Assert.DoesNotMatch("#[0-9A-Fa-f]{6,8}", probeSource);
        Assert.DoesNotMatch("#[0-9A-Fa-f]{6,8}", probeXaml);

        int probeModeIndex = appSource.IndexOf(
            "ButtonStateRuntimeProbeOptions.TryParse",
            StringComparison.Ordinal);
        int dataProfileIndex = appSource.IndexOf(
            "OpenCareerDataProfileSelection.Resolve",
            StringComparison.Ordinal);
        Assert.True(probeModeIndex >= 0);
        Assert.True(dataProfileIndex > probeModeIndex);
        Assert.Contains("LaunchButtonStateRuntimeProbe", appSource, StringComparison.Ordinal);

        Assert.Contains("WindowsAppSDKSelfContained=true", script, StringComparison.Ordinal);
        Assert.Contains("WaitForExit", script, StringComparison.Ordinal);
        Assert.Contains("report.success", script, StringComparison.Ordinal);
        Assert.Contains("[switch]$Interactive", script, StringComparison.Ordinal);
        Assert.Contains(
            "Certify compiled WinUI button states",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "run-button-state-runtime-certification.ps1",
            workflow,
            StringComparison.Ordinal);
    }

    private static void AssertVariantSetter(XDocument document, string styleKey, string expectedVariant)
    {
        XElement style = FindStyle(document, styleKey);
        XElement setter = style
            .Elements()
            .Single(element =>
                element.Name.LocalName == "Setter" &&
                string.Equals(
                    element.Attribute("Property")?.Value,
                    "styles:ButtonStateResources.Variant",
                    StringComparison.Ordinal));

        Assert.Equal(expectedVariant, setter.Attribute("Value")?.Value);
        Assert.DoesNotContain(
            style.Elements().Where(static element => element.Name.LocalName == "Setter"),
            static element => element.Attribute("Property")?.Value is
                "Background" or "Foreground" or "BorderBrush");
    }

    private static XElement FindStyle(XDocument document, string styleKey) =>
        document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style" &&
                string.Equals(element.Attribute(XamlNamespace + "Key")?.Value, styleKey, StringComparison.Ordinal));

    private static IReadOnlyDictionary<string, string> ReadThemeResources(string fileName, string theme)
    {
        XDocument document = XDocument.Load(GetDesignFilePath(fileName));
        XElement themeDictionary = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ResourceDictionary" &&
                string.Equals(element.Attribute(XamlNamespace + "Key")?.Value, theme, StringComparison.Ordinal));

        return themeDictionary
            .Elements()
            .Where(static element => element.Attribute(XamlNamespace + "Key") is not null)
            .ToDictionary(
                element => element.Attribute(XamlNamespace + "Key")!.Value,
                element => element.Attribute("ResourceKey")?.Value
                    ?? throw new InvalidDataException("Button state resource must alias a named brush."),
                StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> ReadNamedBrushColors()
    {
        XDocument document = XDocument.Load(GetDesignFilePath("DesignTokens.xaml"));
        IReadOnlyDictionary<string, string> colors = ReadNamedColors();

        return document
            .Descendants()
            .Where(static element => element.Name.LocalName == "SolidColorBrush")
            .Select(element => new
            {
                Key = element.Attribute(XamlNamespace + "Key")?.Value,
                Color = element.Attribute("Color")?.Value
            })
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Color))
            .ToDictionary(
                static pair => pair.Key!,
                pair => ResolveColor(pair.Color!, colors),
                StringComparer.Ordinal);
    }

    private static string ResolveColor(string value, IReadOnlyDictionary<string, string> colors)
    {
        const string staticResourcePrefix = "{StaticResource ";
        if (value.StartsWith('#'))
            return value;

        if (!value.StartsWith(staticResourcePrefix, StringComparison.Ordinal) || !value.EndsWith('}'))
            throw new InvalidDataException($"Unsupported brush color reference '{value}'.");

        string key = value[staticResourcePrefix.Length..^1];
        return colors[key];
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

    private static bool IsNativeButtonResourceKey(string key) =>
        NativeButtonResourcePrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal));

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
