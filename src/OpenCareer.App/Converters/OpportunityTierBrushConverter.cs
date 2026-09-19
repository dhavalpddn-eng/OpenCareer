using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using OpenCareer.Application.Dashboard;

namespace OpenCareer.App.Converters;

public sealed class OpportunityTierBrushConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        string language)
    {
        string key = value is OpportunityTier tier
            ? tier switch
            {
                OpportunityTier.Specialist => "OpenCareerOpportunitySpecialistBrush",
                OpportunityTier.Elite => "OpenCareerOpportunityEliteBrush",
                OpportunityTier.Legendary => "OpenCareerOpportunityLegendaryBrush",
                _ => "OpenCareerOpportunityStandardBrush"
            }
            : "OpenCareerOpportunityStandardBrush";

        return Application.Current.Resources[key] as Brush ??
            Application.Current.Resources["OpenCareerNavyBrush"];
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        string language) =>
        throw new NotSupportedException();
}
