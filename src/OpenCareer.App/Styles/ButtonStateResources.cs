using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenCareer.App.Styles;

public enum OpenCareerButtonVariant
{
    None = 0,
    Primary = 1,
    Secondary = 2,
    Danger = 3
}

public static class ButtonStateResources
{
    private static readonly ConditionalWeakTable<Button, ResourceDictionary> AppliedResources = new();
    private static string _runtimeDiagnosticSnapshot =
        "ButtonStateResources.Variant callback was not reached.";

    internal static string RuntimeDiagnosticSnapshot =>
        Volatile.Read(ref _runtimeDiagnosticSnapshot);

    public static readonly DependencyProperty VariantProperty = DependencyProperty.RegisterAttached(
        "Variant",
        typeof(OpenCareerButtonVariant),
        typeof(ButtonStateResources),
        new PropertyMetadata(OpenCareerButtonVariant.None, OnVariantChanged));

    public static OpenCareerButtonVariant GetVariant(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (OpenCareerButtonVariant)element.GetValue(VariantProperty);
    }

    public static void SetVariant(DependencyObject element, OpenCareerButtonVariant value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(VariantProperty, value);
    }

    private static void OnVariantChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        RecordDiagnostic(
            $"Variant callback entered; target={dependencyObject.GetType().FullName}; " +
            $"old={args.OldValue}; new={args.NewValue}.");

        if (dependencyObject is not Button button)
        {
            RecordDiagnostic("Variant callback ignored a non-Button target.");
            return;
        }

        if (AppliedResources.TryGetValue(button, out ResourceDictionary? previousResources))
        {
            RecordDiagnostic("Removing the previously applied per-button state dictionary.");
            button.Resources.MergedDictionaries.Remove(previousResources);
            AppliedResources.Remove(button);
        }

        OpenCareerButtonVariant variant = (OpenCareerButtonVariant)args.NewValue;
        if (variant == OpenCareerButtonVariant.None)
        {
            RecordDiagnostic("Variant=None completed without loading a state dictionary.");
            return;
        }

        var source = new Uri(
            $"ms-appx:///Styles/{variant}ButtonStates.xaml",
            UriKind.Absolute);
        var variantResources = new ResourceDictionary();

        RecordDiagnostic(
            $"Variant={variant}; stage=ResourceDictionary.Source assignment; source={source}.");
        try
        {
            variantResources.Source = source;
        }
        catch (Exception ex)
        {
            RecordDiagnostic(
                $"Variant={variant}; stage=ResourceDictionary.Source assignment FAILED; " +
                DescribeException(ex));
            throw;
        }

        RecordDiagnostic(
            $"Variant={variant}; stage=add loaded dictionary to Button.Resources.");
        button.Resources.MergedDictionaries.Add(variantResources);
        AppliedResources.Add(button, variantResources);
        RecordDiagnostic(
            $"Variant={variant}; stage=completed; localDictionaryCount=" +
            $"{button.Resources.MergedDictionaries.Count}.");
    }

    private static void RecordDiagnostic(string value) =>
        Volatile.Write(ref _runtimeDiagnosticSnapshot, value);

    private static string DescribeException(Exception exception)
    {
        string inner = exception.InnerException is null
            ? "none"
            : $"{exception.InnerException.GetType().FullName}: " +
              $"{exception.InnerException.Message}; " +
              $"HRESULT=0x{unchecked((uint)exception.InnerException.HResult):X8}";

        return $"{exception.GetType().FullName}: {exception.Message}; " +
            $"HRESULT=0x{unchecked((uint)exception.HResult):X8}; inner={inner}.";
    }
}
