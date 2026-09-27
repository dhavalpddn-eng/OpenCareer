using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenCareer.App.Styles;
using XamlApplication = Microsoft.UI.Xaml.Application;

namespace OpenCareer.App.Diagnostics;

internal sealed record ButtonStateRuntimeProbeOptions(
    string OutputPath,
    bool AutoClose)
{
    public const string LaunchArgument = "--dev-button-state-runtime-probe";
    public const string OutputArgument = "--button-state-probe-output";
    public const string AutoCloseArgument = "--button-state-probe-auto-close";

    public static ButtonStateRuntimeProbeOptions? TryParse(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (!arguments.Any(argument => string.Equals(
                argument,
                LaunchArgument,
                StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        string? outputPath = null;
        for (int index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(
                    arguments[index],
                    OutputArgument,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= arguments.Count ||
                string.IsNullOrWhiteSpace(arguments[index + 1]))
            {
                throw new ArgumentException(
                    $"{OutputArgument} requires a file path.",
                    nameof(arguments));
            }

            outputPath = arguments[index + 1];
            break;
        }

        outputPath ??= Path.Combine(
            Path.GetTempPath(),
            "OpenCareer-button-state-runtime-probe.json");

        return new ButtonStateRuntimeProbeOptions(
            Path.GetFullPath(outputPath),
            arguments.Any(argument => string.Equals(
                argument,
                AutoCloseArgument,
                StringComparison.OrdinalIgnoreCase)));
    }
}

public sealed partial class ButtonStateRuntimeProbePage : Page
{
    public ButtonStateRuntimeProbePage()
    {
        InitializeComponent();
    }

    public Button PrimaryButton => PrimaryProbeButton;
    public Button SecondaryButton => SecondaryProbeButton;
    public Button DangerButton => DangerProbeButton;
    public Button StockLightButton => StockLightProbeButton;
    public Button StockDarkButton => StockDarkProbeButton;
    public TextBlock StatusText => ProbeStatusText;

    internal SolidColorBrush GetExpectedBrush(string token)
    {
        string alias = $"Probe{token}";
        if (Resources.TryGetValue(alias, out object? value) &&
            value is SolidColorBrush brush)
        {
            return brush;
        }

        throw new InvalidDataException(
            $"Compiled probe resource alias '{alias}' did not resolve to a SolidColorBrush.");
    }

    internal void ShowResult(ButtonStateRuntimeProbeReport report)
    {
        StatusText.Text = report.Success
            ? "PASS — runtime resources, stock template, and all four visual states resolved."
            : $"FAIL — {report.Failure}";
    }

    private void DisabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        bool isEnabled = !DisabledToggle.IsOn;
        PrimaryButton.IsEnabled = isEnabled;
        SecondaryButton.IsEnabled = isEnabled;
        DangerButton.IsEnabled = isEnabled;
    }
}

internal sealed record ButtonStateRuntimeProbeReport(
    int SchemaVersion,
    bool Success,
    string WindowsAppSdkAssemblyVersion,
    IReadOnlyList<ButtonVariantRuntimeProbeResult> Variants,
    bool VariantResourcesIsolated,
    bool StockTemplatePreserved,
    bool SystemFocusBehaviorPreserved,
    bool NativeButtonAutomationPreserved,
    string? Failure);

internal sealed record ButtonVariantRuntimeProbeResult(
    string Variant,
    string StyleKey,
    string ResourceDictionarySource,
    IReadOnlyList<ButtonVisualStateRuntimeProbeResult> States);

internal sealed record ButtonVisualStateRuntimeProbeResult(
    string State,
    string BackgroundToken,
    string ForegroundToken,
    string BorderToken,
    string ResolvedBackground,
    string ResolvedForeground,
    string ResolvedBorder);

internal static class ButtonStateRuntimeProbe
{
    private const int ReportSchemaVersion = 1;

    private static readonly VariantExpectation[] Expectations =
    [
        new(
            OpenCareerButtonVariant.Primary,
            "OpenCareerPrimaryButtonStyle",
            ElementTheme.Dark,
            new Dictionary<string, StateBrushExpectation>(StringComparer.Ordinal)
            {
                ["Normal"] = new(
                    "OpenCareerNavyBrush",
                    "OpenCareerShellTextBrush",
                    "OpenCareerSteelBorderBrush"),
                ["PointerOver"] = new(
                    "OpenCareerNavyRaisedBrush",
                    "OpenCareerShellTextBrush",
                    "OpenCareerSteelHighlightBrush"),
                ["Pressed"] = new(
                    "OpenCareerNavyPressedBrush",
                    "OpenCareerShellTextBrush",
                    "OpenCareerNavyPressedBrush"),
                ["Disabled"] = new(
                    "OpenCareerInkMutedBrush",
                    "OpenCareerPaperMutedBrush",
                    "OpenCareerDisabledBrush")
            }),
        new(
            OpenCareerButtonVariant.Secondary,
            "OpenCareerSecondaryButtonStyle",
            ElementTheme.Light,
            new Dictionary<string, StateBrushExpectation>(StringComparer.Ordinal)
            {
                ["Normal"] = new(
                    "OpenCareerPaperAltBrush",
                    "OpenCareerInkBrush",
                    "OpenCareerPaperRuleBrush"),
                ["PointerOver"] = new(
                    "OpenCareerSelectionBrush",
                    "OpenCareerInkBrush",
                    "OpenCareerNavyRaisedBrush"),
                ["Pressed"] = new(
                    "OpenCareerSecondaryPressedBrush",
                    "OpenCareerInkBrush",
                    "OpenCareerNavyBrush"),
                ["Disabled"] = new(
                    "OpenCareerPaperMutedBrush",
                    "OpenCareerInkMutedBrush",
                    "OpenCareerPaperRuleBrush")
            }),
        new(
            OpenCareerButtonVariant.Danger,
            "OpenCareerDangerButtonStyle",
            ElementTheme.Dark,
            new Dictionary<string, StateBrushExpectation>(StringComparer.Ordinal)
            {
                ["Normal"] = new(
                    "OpenCareerDangerBrush",
                    "OpenCareerShellTextBrush",
                    "OpenCareerDangerBrush"),
                ["PointerOver"] = new(
                    "OpenCareerDangerRaisedBrush",
                    "OpenCareerShellTextBrush",
                    "OpenCareerDangerRaisedBrush"),
                ["Pressed"] = new(
                    "OpenCareerDangerPressedBrush",
                    "OpenCareerShellTextBrush",
                    "OpenCareerDangerPressedBrush"),
                ["Disabled"] = new(
                    "OpenCareerDangerDisabledBrush",
                    "OpenCareerPaperMutedBrush",
                    "OpenCareerDisabledBrush")
            })
    ];

    public static async Task<ButtonStateRuntimeProbeReport> RunAsync(
        ButtonStateRuntimeProbePage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        string windowsAppSdkVersion =
            typeof(XamlApplication).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? typeof(XamlApplication).Assembly.GetName().Version?.ToString()
            ?? "unknown";

        try
        {
            page.UpdateLayout();

            Button[] buttons =
            [
                page.PrimaryButton,
                page.SecondaryButton,
                page.DangerButton,
                page.StockLightButton,
                page.StockDarkButton
            ];

            foreach (Button button in buttons)
            {
                if (!button.ApplyTemplate())
                    button.UpdateLayout();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
            page.UpdateLayout();

            IReadOnlyList<ButtonVariantRuntimeProbeResult> variants =
            [
                await CertifyVariantAsync(page, page.PrimaryButton, Expectations[0]),
                await CertifyVariantAsync(page, page.SecondaryButton, Expectations[1]),
                await CertifyVariantAsync(page, page.DangerButton, Expectations[2])
            ];

            AssertVariantsAreIsolated(variants);
            await AssertVariantSwitchDoesNotLeakAsync(page, page.PrimaryButton);

            Style stockStyle = page.StockLightButton.Style
                ?? throw new InvalidDataException(
                    "Compiled probe XAML did not apply DefaultButtonStyle to its stock reference Button.");
            if (!ReferenceEquals(stockStyle, page.StockDarkButton.Style))
            {
                throw new InvalidDataException(
                    "The light and dark stock reference Buttons did not resolve the same DefaultButtonStyle.");
            }
            AssertStockStyleChain(page.PrimaryButton, stockStyle);
            AssertStockStyleChain(page.SecondaryButton, stockStyle);
            AssertStockStyleChain(page.DangerButton, stockStyle);

            if (!ReferenceEquals(page.PrimaryButton.Template, page.StockDarkButton.Template) ||
                !ReferenceEquals(page.SecondaryButton.Template, page.StockLightButton.Template) ||
                !ReferenceEquals(page.DangerButton.Template, page.StockDarkButton.Template))
            {
                throw new InvalidDataException(
                    "An OpenCareer Button does not retain the stock DefaultButtonStyle template.");
            }

            AssertFocusBehaviorMatchesStock(page.PrimaryButton, page.StockDarkButton);
            AssertFocusBehaviorMatchesStock(page.SecondaryButton, page.StockLightButton);
            AssertFocusBehaviorMatchesStock(page.DangerButton, page.StockDarkButton);

            AssertNativeButtonAutomation(page.PrimaryButton);
            AssertNativeButtonAutomation(page.SecondaryButton);
            AssertNativeButtonAutomation(page.DangerButton);

            return new ButtonStateRuntimeProbeReport(
                ReportSchemaVersion,
                true,
                windowsAppSdkVersion,
                variants,
                VariantResourcesIsolated: true,
                StockTemplatePreserved: true,
                SystemFocusBehaviorPreserved: true,
                NativeButtonAutomationPreserved: true,
                Failure: null);
        }
        catch (Exception ex)
        {
            return CreateFailureReport(ex, windowsAppSdkVersion);
        }
    }

    public static ButtonStateRuntimeProbeReport CreateFailureReport(
        Exception exception,
        string windowsAppSdkVersion = "unavailable")
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new ButtonStateRuntimeProbeReport(
            ReportSchemaVersion,
            false,
            windowsAppSdkVersion,
            [],
            VariantResourcesIsolated: false,
            StockTemplatePreserved: false,
            SystemFocusBehaviorPreserved: false,
            NativeButtonAutomationPreserved: false,
            Failure: exception.ToString());
    }

    public static void WriteReport(
        string outputPath,
        ButtonStateRuntimeProbeReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(report);

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(
            outputPath,
            JsonSerializer.Serialize(
                report,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));
    }

    private static async Task<ButtonVariantRuntimeProbeResult> CertifyVariantAsync(
        ButtonStateRuntimeProbePage page,
        Button button,
        VariantExpectation expectation)
    {
        if (ButtonStateResources.GetVariant(button) != expectation.Variant)
        {
            throw new InvalidDataException(
                $"{expectation.Variant} style did not set its attached Variant value.");
        }

        if (button.RequestedTheme != expectation.RequestedTheme)
        {
            throw new InvalidDataException(
                $"{expectation.Variant} requested theme was {button.RequestedTheme}, expected {expectation.RequestedTheme}.");
        }

        string expectedSource =
            $"ms-appx:///Styles/{expectation.Variant}ButtonStates.xaml";
        AssertOnlyVariantDictionary(button, expectedSource);

        ContentPresenter presenter = FindStockContentPresenter(button);
        var states = new List<ButtonVisualStateRuntimeProbeResult>();

        foreach ((string state, StateBrushExpectation brushes) in expectation.States)
        {
            button.IsEnabled = state != "Disabled";
            if (!VisualStateManager.GoToState(button, state, useTransitions: false))
            {
                throw new InvalidDataException(
                    $"The stock Button template does not expose the '{state}' visual state.");
            }

            // The stock template declares an 83 ms BrushTransition. Waiting one
            // bounded interval verifies the effective presenter brush, not only
            // the state request.
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            button.UpdateLayout();

            SolidColorBrush actualBackground =
                RequireSolidBrush(presenter.Background, expectation.Variant, state, "Background");
            SolidColorBrush actualForeground =
                RequireSolidBrush(presenter.Foreground, expectation.Variant, state, "Foreground");
            SolidColorBrush actualBorder =
                RequireSolidBrush(presenter.BorderBrush, expectation.Variant, state, "BorderBrush");

            AssertBrushMatchesToken(
                page,
                actualBackground,
                brushes.BackgroundToken,
                expectation.Variant,
                state,
                "Background");
            AssertBrushMatchesToken(
                page,
                actualForeground,
                brushes.ForegroundToken,
                expectation.Variant,
                state,
                "Foreground");
            AssertBrushMatchesToken(
                page,
                actualBorder,
                brushes.BorderToken,
                expectation.Variant,
                state,
                "BorderBrush");

            states.Add(new ButtonVisualStateRuntimeProbeResult(
                state,
                brushes.BackgroundToken,
                brushes.ForegroundToken,
                brushes.BorderToken,
                actualBackground.Color.ToString(),
                actualForeground.Color.ToString(),
                actualBorder.Color.ToString()));
        }

        button.IsEnabled = true;
        if (!VisualStateManager.GoToState(button, "Normal", useTransitions: false))
            throw new InvalidDataException("The stock Button template could not return to Normal.");

        return new ButtonVariantRuntimeProbeResult(
            expectation.Variant.ToString(),
            expectation.StyleKey,
            expectedSource,
            states);
    }

    private static void AssertVariantsAreIsolated(
        IReadOnlyList<ButtonVariantRuntimeProbeResult> variants)
    {
        foreach (string state in new[] { "Normal", "PointerOver", "Pressed", "Disabled" })
        {
            string[] backgrounds = variants
                .Select(variant => variant.States.Single(item => item.State == state).ResolvedBackground)
                .ToArray();

            if (backgrounds.Distinct(StringComparer.Ordinal).Count() != backgrounds.Length)
            {
                throw new InvalidDataException(
                    $"The {state} background leaked across Button variants.");
            }
        }
    }

    private static async Task AssertVariantSwitchDoesNotLeakAsync(
        ButtonStateRuntimeProbePage page,
        Button button)
    {
        OpenCareerButtonVariant[] sequence =
        [
            OpenCareerButtonVariant.Secondary,
            OpenCareerButtonVariant.Danger,
            OpenCareerButtonVariant.None,
            OpenCareerButtonVariant.Primary,
            OpenCareerButtonVariant.Primary
        ];

        foreach (OpenCareerButtonVariant variant in sequence)
        {
            ButtonStateResources.SetVariant(button, variant);
            await Task.Yield();

            if (variant == OpenCareerButtonVariant.None)
            {
                if (button.Resources.MergedDictionaries.Count != 0)
                {
                    throw new InvalidDataException(
                        "Clearing a Button variant left a stale local ResourceDictionary.");
                }

                continue;
            }

            AssertOnlyVariantDictionary(
                button,
                $"ms-appx:///Styles/{variant}ButtonStates.xaml");
        }

        button.ApplyTemplate();
        _ = await CertifyVariantAsync(page, button, Expectations[0]);
    }

    private static void AssertOnlyVariantDictionary(
        Button button,
        string expectedSource)
    {
        if (button.Resources.MergedDictionaries.Count != 1)
        {
            throw new InvalidDataException(
                $"Button has {button.Resources.MergedDictionaries.Count} local merged dictionaries; expected exactly one variant dictionary.");
        }

        string actualSource =
            button.Resources.MergedDictionaries[0].Source?.AbsoluteUri
            ?? string.Empty;
        if (!string.Equals(actualSource, expectedSource, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Button state dictionary was '{actualSource}', expected '{expectedSource}'.");
        }
    }

    private static void AssertStockStyleChain(Button button, Style stockStyle)
    {
        Style? baseStyle = button.Style?.BasedOn;
        if (baseStyle is null || !ReferenceEquals(baseStyle.BasedOn, stockStyle))
        {
            throw new InvalidDataException(
                "OpenCareer Button style does not terminate at DefaultButtonStyle.");
        }
    }

    private static void AssertFocusBehaviorMatchesStock(
        Button button,
        Button stockButton)
    {
        if (button.UseSystemFocusVisuals != stockButton.UseSystemFocusVisuals ||
            !button.FocusVisualMargin.Equals(stockButton.FocusVisualMargin) ||
            !button.FocusVisualPrimaryThickness.Equals(stockButton.FocusVisualPrimaryThickness) ||
            !button.FocusVisualSecondaryThickness.Equals(stockButton.FocusVisualSecondaryThickness) ||
            !BrushesMatch(button.FocusVisualPrimaryBrush, stockButton.FocusVisualPrimaryBrush) ||
            !BrushesMatch(button.FocusVisualSecondaryBrush, stockButton.FocusVisualSecondaryBrush))
        {
            throw new InvalidDataException(
                "OpenCareer Button focus behavior differs from DefaultButtonStyle.");
        }
    }

    private static bool BrushesMatch(Brush? left, Brush? right)
    {
        if (left is SolidColorBrush leftSolid && right is SolidColorBrush rightSolid)
        {
            return leftSolid.Color == rightSolid.Color &&
                leftSolid.Opacity == rightSolid.Opacity;
        }

        return left?.GetType() == right?.GetType();
    }

    private static void AssertNativeButtonAutomation(Button button)
    {
        AutomationPeer? peer =
            FrameworkElementAutomationPeer.CreatePeerForElement(button);
        if (peer is not ButtonAutomationPeer ||
            peer.GetAutomationControlType() != AutomationControlType.Button ||
            string.IsNullOrWhiteSpace(peer.GetName()) ||
            peer.GetPattern(PatternInterface.Invoke) is null)
        {
            throw new InvalidDataException(
                "OpenCareer Button no longer exposes native Button automation behavior.");
        }
    }

    private static ContentPresenter FindStockContentPresenter(Button button)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(button);

        while (queue.Count > 0)
        {
            DependencyObject current = queue.Dequeue();
            if (current is ContentPresenter presenter &&
                string.Equals(presenter.Name, "ContentPresenter", StringComparison.Ordinal))
            {
                return presenter;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(current);
            for (int index = 0; index < childCount; index++)
                queue.Enqueue(VisualTreeHelper.GetChild(current, index));
        }

        throw new InvalidDataException(
            "DefaultButtonStyle did not create its stock ContentPresenter template root.");
    }

    private static SolidColorBrush RequireSolidBrush(
        Brush? brush,
        OpenCareerButtonVariant variant,
        string state,
        string property)
    {
        return brush as SolidColorBrush
            ?? throw new InvalidDataException(
                $"{variant} {state} {property} did not resolve to a SolidColorBrush.");
    }

    private static void AssertBrushMatchesToken(
        ButtonStateRuntimeProbePage page,
        SolidColorBrush actual,
        string expectedToken,
        OpenCareerButtonVariant variant,
        string state,
        string property)
    {
        SolidColorBrush expected = page.GetExpectedBrush(expectedToken);

        if (actual.Color != expected.Color || actual.Opacity != expected.Opacity)
        {
            throw new InvalidDataException(
                $"{variant} {state} {property} resolved to {actual.Color}/{actual.Opacity}, expected token {expectedToken}={expected.Color}/{expected.Opacity}.");
        }
    }

    private sealed record VariantExpectation(
        OpenCareerButtonVariant Variant,
        string StyleKey,
        ElementTheme RequestedTheme,
        IReadOnlyDictionary<string, StateBrushExpectation> States);

    private sealed record StateBrushExpectation(
        string BackgroundToken,
        string ForegroundToken,
        string BorderToken);
}
