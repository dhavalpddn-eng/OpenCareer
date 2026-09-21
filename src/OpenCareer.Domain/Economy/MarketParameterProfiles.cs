namespace OpenCareer.Domain.Economy;

public static class MarketParameterProfiles
{
    public static MarketParameters For(MarketSegment segment) => segment switch
    {
        MarketSegment.GeneralPassenger => new(
            PriceElasticity: 0.75,
            BacklogRetentionPerDay: 0.20,
            PriceHalfLifeDays: 3.0,
            BacklogPriceWeight: 0.10,
            TargetMarkup: 0.14),

        MarketSegment.BusinessCharter => new(
            PriceElasticity: 0.40,
            BacklogRetentionPerDay: 0.35,
            PriceHalfLifeDays: 2.0,
            BacklogPriceWeight: 0.20,
            TargetMarkup: 0.30),

        MarketSegment.LeisureCharter => new(
            PriceElasticity: 0.65,
            BacklogRetentionPerDay: 0.25,
            PriceHalfLifeDays: 3.0,
            BacklogPriceWeight: 0.15,
            TargetMarkup: 0.24),

        MarketSegment.GeneralCargo => new(
            PriceElasticity: 0.25,
            BacklogRetentionPerDay: 0.90,
            PriceHalfLifeDays: 5.0,
            BacklogPriceWeight: 0.50,
            TargetMarkup: 0.20),

        MarketSegment.ExpressCargo => new(
            PriceElasticity: 0.15,
            BacklogRetentionPerDay: 0.72,
            PriceHalfLifeDays: 2.5,
            BacklogPriceWeight: 0.70,
            TargetMarkup: 0.30),

        MarketSegment.MedicalLogistics => new(
            PriceElasticity: 0.05,
            BacklogRetentionPerDay: 0.30,
            PriceHalfLifeDays: 1.0,
            BacklogPriceWeight: 0.90,
            TargetMarkup: 0.38),

        MarketSegment.Training => new(
            PriceElasticity: 0.60,
            BacklogRetentionPerDay: 0.10,
            PriceHalfLifeDays: 7.0,
            TargetMarkup: 0.20),

        MarketSegment.Agriculture => new(
            PriceElasticity: 0.20,
            BacklogRetentionPerDay: 0.65,
            PriceHalfLifeDays: 3.0,
            BacklogPriceWeight: 0.55,
            TargetMarkup: 0.28),

        MarketSegment.Firefighting => new(
            PriceElasticity: 0.03,
            BacklogRetentionPerDay: 0.25,
            PriceHalfLifeDays: 0.75,
            BacklogPriceWeight: 1.10,
            TargetMarkup: 0.55),

        MarketSegment.Survey => new(
            PriceElasticity: 0.25,
            BacklogRetentionPerDay: 0.55,
            PriceHalfLifeDays: 5.0,
            TargetMarkup: 0.25),

        MarketSegment.Maintenance => new(
            PriceElasticity: 0.30,
            BacklogRetentionPerDay: 0.88,
            PriceHalfLifeDays: 6.0,
            BacklogPriceWeight: 0.55,
            TargetMarkup: 0.22),

        MarketSegment.GovernmentPriority => new(
            PriceElasticity: 0.02,
            BacklogRetentionPerDay: 0.45,
            PriceHalfLifeDays: 1.0,
            BacklogPriceWeight: 0.95,
            TargetMarkup: 0.40),

        MarketSegment.MilitarySupport => new(
            PriceElasticity: 0.02,
            BacklogRetentionPerDay: 0.40,
            PriceHalfLifeDays: 1.0,
            BacklogPriceWeight: 0.95,
            TargetMarkup: 0.45),

        _ => new MarketParameters()
    };
}
