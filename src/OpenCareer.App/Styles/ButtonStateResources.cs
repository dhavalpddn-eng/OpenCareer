using System.Runtime.CompilerServices;
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
        if (dependencyObject is not Button button)
            return;

        if (AppliedResources.TryGetValue(button, out ResourceDictionary? previousResources))
        {
            button.Resources.MergedDictionaries.Remove(previousResources);
            AppliedResources.Remove(button);
        }

        OpenCareerButtonVariant variant = (OpenCareerButtonVariant)args.NewValue;
        if (variant == OpenCareerButtonVariant.None)
            return;

        ResourceDictionary variantResources = new()
        {
            Source = new Uri($"ms-appx:///Styles/{variant}ButtonStates.xaml", UriKind.Absolute)
        };

        button.Resources.MergedDictionaries.Add(variantResources);
        AppliedResources.Add(button, variantResources);
    }
}
