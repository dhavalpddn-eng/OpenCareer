using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace OpenCareer.App.Diagnostics;

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
        string elementName = $"Expected{token}";
        if (FindName(elementName) is Border border &&
            border.Background is SolidColorBrush brush)
        {
            return brush;
        }

        throw new InvalidDataException(
            $"Compiled probe element '{elementName}' did not resolve to a SolidColorBrush.");
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
