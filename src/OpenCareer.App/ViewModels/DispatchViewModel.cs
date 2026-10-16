using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Planning;

namespace OpenCareer.App.ViewModels;

public sealed record DispatchPlanningFormInput(
    string OriginIcao,
    string DestinationIcao,
    string PayloadPounds,
    string TotalRouteDistanceNauticalMiles,
    string CruiseSegmentDistanceNauticalMiles,
    string PlannedCruiseWeightPounds,
    string IsaDeviationCelsius,
    string PressureAltitudeFeet,
    string ClimbTimeMinutes,
    string DescentTimeMinutes,
    string ClimbFuelGallons,
    string DescentFuelGallons,
    string ContingencyPercent,
    string FinalReserveMinutes,
    string AdditionalReserveFuelGallons,
    string RangeSafetyMarginPercent,
    string RunwaySafetyMarginPercent,
    bool ApplyWeatherLimits,
    string MaximumCrosswindKnots,
    string MaximumTailwindKnots,
    string MaximumDensityAltitudeFeet,
    string MinimumVisibilityStatuteMiles,
    string MinimumCeilingFeetAgl);

public sealed class DispatchViewModel : INotifyPropertyChanged
{
    private readonly IInstalledAircraftInventorySource _aircraftInventory;
    private readonly IDispatchCruiseProfileSource _cruiseProfiles;
    private readonly ICompositeDispatchPlanningService _planning;
    private readonly IAcceptedDispatchPlanningContextSource _acceptedContext;
    private IReadOnlyList<DispatchAircraftOptionViewModel> _aircraftOptions =
        Array.Empty<DispatchAircraftOptionViewModel>();
    private string? _selectedAircraftId;
    private IReadOnlyList<DispatchCruiseProfileOptionViewModel> _cruiseProfileOptions =
        Array.Empty<DispatchCruiseProfileOptionViewModel>();
    private int? _selectedCruiseProfileIndex;
    private string _selectedCruiseProfileEvidence = "No cruise profile selected.";
    private string _cruiseProfileStatus =
        "Select an installed aircraft to load authoritative cruise profiles.";
    private string _inventoryStatus = "Installed aircraft have not been loaded.";
    private string _acceptedContextText =
        "Accepted-job dispatch context has not been loaded.";
    private string? _acceptedOriginIcao;
    private string? _acceptedDestinationIcao;
    private string? _acceptedPayloadPoundsText;
    private string? _acceptedRouteDistanceText;
    private double? _acceptedPayloadPounds;
    private double? _acceptedRouteDistanceNauticalMiles;
    private string? _acceptedAircraftId;
    private string? _acceptedReservationId;
    private bool _isAcceptedContextLocked;
    private string _resultStatus = "NOT EVALUATED";
    private string _resultDetail =
        "Enter explicit planning inputs, then evaluate. No operation is created or started here.";
    private string _fuelSummary = "Fuel plan —";
    private string _runwaySummary = "Runway selection —";
    private IReadOnlyList<DispatchIssueViewModel> _issues =
        Array.Empty<DispatchIssueViewModel>();
    private bool _isBusy;

    public DispatchViewModel(
        IInstalledAircraftInventorySource aircraftInventory,
        IDispatchCruiseProfileSource cruiseProfiles,
        ICompositeDispatchPlanningService planning,
        IAcceptedDispatchPlanningContextSource acceptedContext)
    {
        _aircraftInventory = aircraftInventory
            ?? throw new ArgumentNullException(nameof(aircraftInventory));
        _cruiseProfiles = cruiseProfiles
            ?? throw new ArgumentNullException(nameof(cruiseProfiles));
        _planning = planning
            ?? throw new ArgumentNullException(nameof(planning));
        _acceptedContext = acceptedContext
            ?? throw new ArgumentNullException(nameof(acceptedContext));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<DispatchAircraftOptionViewModel> AircraftOptions => _aircraftOptions;
    public string? SelectedAircraftId => _selectedAircraftId;
    public IReadOnlyList<DispatchCruiseProfileOptionViewModel> CruiseProfileOptions =>
        _cruiseProfileOptions;
    public int? SelectedCruiseProfileIndex => _selectedCruiseProfileIndex;
    public string CruiseProfileStatus => _cruiseProfileStatus;
    public string SelectedCruiseProfileEvidence => _selectedCruiseProfileEvidence;
    public string InventoryStatus => _inventoryStatus;
    public string AcceptedContextText => _acceptedContextText;
    public string? AcceptedOriginIcao => _acceptedOriginIcao;
    public string? AcceptedDestinationIcao => _acceptedDestinationIcao;
    public string? AcceptedPayloadPoundsText => _acceptedPayloadPoundsText;
    public string? AcceptedRouteDistanceText => _acceptedRouteDistanceText;
    public bool IsAcceptedContextLocked => _isAcceptedContextLocked;
    public string ResultStatus => _resultStatus;
    public string ResultDetail => _resultDetail;
    public string FuelSummary => _fuelSummary;
    public string RunwaySummary => _runwaySummary;
    public IReadOnlyList<DispatchIssueViewModel> Issues => _issues;
    public bool IsBusy => _isBusy;
    public bool CanEvaluate =>
        !_isBusy
        && _selectedAircraftId is not null
        && _selectedCruiseProfileIndex is not null;
    public string EvaluateButtonText => _isBusy ? "Evaluating…" : "Evaluate Plan";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        InstalledAircraftInventorySnapshot snapshot = await _aircraftInventory
            .ReadAsync(cancellationToken)
            .ConfigureAwait(true);

        SetField(
            ref _aircraftOptions,
            snapshot.Aircraft
                .Select(static item => new DispatchAircraftOptionViewModel(
                    item.CanonicalAircraftId,
                    item.DisplayName))
                .ToArray(),
            nameof(AircraftOptions));
        SetField(ref _inventoryStatus, snapshot.Detail, nameof(InventoryStatus));

        AcceptedDispatchPlanningContext accepted = await _acceptedContext
            .ReadAsync(cancellationToken)
            .ConfigureAwait(true);
        ApplyAcceptedContext(accepted);

        string? preferred = _acceptedAircraftId ?? _selectedAircraftId;
        string? selection = _aircraftOptions.Any(option => string.Equals(
                option.AircraftId,
                preferred,
                StringComparison.OrdinalIgnoreCase))
            ? preferred
            : _aircraftOptions.FirstOrDefault()?.AircraftId;
        await SetSelectedAircraftAsync(selection, cancellationToken, forceReload: true)
            .ConfigureAwait(true);

        if (_acceptedAircraftId is not null
            && !string.Equals(
                selection,
                _acceptedAircraftId,
                StringComparison.OrdinalIgnoreCase))
        {
            SetField(
                ref _acceptedContextText,
                "The accepted job's reserved aircraft is not in current installed discovery. Its reservation will not authorize another aircraft.",
                nameof(AcceptedContextText));
            _acceptedReservationId = null;
        }
    }

    public async Task SetSelectedAircraftAsync(
        string? aircraftId,
        CancellationToken cancellationToken = default,
        bool forceReload = false)
    {
        string? normalized = string.IsNullOrWhiteSpace(aircraftId)
            ? null
            : aircraftId.Trim();

        if (normalized is not null
            && !_aircraftOptions.Any(option => string.Equals(
                option.AircraftId,
                normalized,
                StringComparison.OrdinalIgnoreCase)))
        {
            normalized = null;
        }

        if (!forceReload
            && string.Equals(
                _selectedAircraftId,
                normalized,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SetField(
            ref _selectedAircraftId,
            normalized,
            nameof(SelectedAircraftId));
        SetField(
            ref _cruiseProfileOptions,
            Array.Empty<DispatchCruiseProfileOptionViewModel>(),
            nameof(CruiseProfileOptions));
        SetField(
            ref _selectedCruiseProfileIndex,
            null,
            nameof(SelectedCruiseProfileIndex));
        SetField(
            ref _selectedCruiseProfileEvidence,
            "No cruise profile selected.",
            nameof(SelectedCruiseProfileEvidence));
        SetField(
            ref _cruiseProfileStatus,
            normalized is null
                ? "Select an installed aircraft to load authoritative cruise profiles."
                : "Loading authoritative cruise profiles…",
            nameof(CruiseProfileStatus));
        OnPropertyChanged(nameof(CanEvaluate));

        if (normalized is null)
            return;

        DispatchCruiseProfileCatalog catalog = await _cruiseProfiles
            .ReadAsync(normalized, cancellationToken)
            .ConfigureAwait(true);

        if (!string.Equals(
                _selectedAircraftId,
                normalized,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SetField(
            ref _cruiseProfileOptions,
            catalog.Profiles
                .Select(static profile => new DispatchCruiseProfileOptionViewModel(
                    profile.ProfileIndex,
                    profile.FuelTypeIndex,
                    profile.DisplayName,
                    profile.Envelope,
                    profile.EvidenceDetail))
                .ToArray(),
            nameof(CruiseProfileOptions));
        SetField(
            ref _cruiseProfileStatus,
            catalog.Detail,
            nameof(CruiseProfileStatus));
    }

    public void SetSelectedCruiseProfile(int? profileIndex)
    {
        int? normalized = profileIndex is { } index
            && _cruiseProfileOptions.Any(profile => profile.ProfileIndex == index)
                ? index
                : null;

        if (SetField(
                ref _selectedCruiseProfileIndex,
                normalized,
                nameof(SelectedCruiseProfileIndex)))
        {
            string evidence = normalized is { } selected
                ? _cruiseProfileOptions
                    .Single(profile => profile.ProfileIndex == selected)
                    .EvidenceDetail
                : "No cruise profile selected.";
            SetField(
                ref _selectedCruiseProfileEvidence,
                evidence,
                nameof(SelectedCruiseProfileEvidence));
            OnPropertyChanged(nameof(CanEvaluate));
        }
    }

    public async Task EvaluateAsync(
        DispatchPlanningFormInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (_isBusy)
            return;

        if (_selectedAircraftId is null)
        {
            SetResult(
                "NOT EVALUATED",
                "Select an installed aircraft before evaluating dispatch.",
                "Fuel plan —",
                "Runway selection —",
                []);
            return;
        }

        DispatchCruiseProfileOptionViewModel? selectedProfile =
            _cruiseProfileOptions.SingleOrDefault(profile =>
                profile.ProfileIndex == _selectedCruiseProfileIndex);
        if (selectedProfile is null)
        {
            SetResult(
                "NOT EVALUATED",
                "Select an authoritative cruise profile before evaluating dispatch.",
                "Fuel plan —",
                "Runway selection —",
                []);
            return;
        }

        SetBusy(true);
        try
        {
            bool useAcceptedReservation =
                _acceptedReservationId is not null
                && string.Equals(
                    _selectedAircraftId,
                    _acceptedAircraftId,
                    StringComparison.OrdinalIgnoreCase);

            if (useAcceptedReservation
                && (_acceptedPayloadPounds is null
                    || _acceptedRouteDistanceNauticalMiles is null))
            {
                SetResult(
                    "ACCEPTED PLAN UNAVAILABLE",
                    "The accepted contract has no complete authoritative payload and route-distance terms. Its Fleet reservation cannot prepare a dispatch plan.",
                    "Fuel plan —",
                    "Runway selection —",
                    []);
                return;
            }

            ParsedDispatchInput parsed;
            try
            {
                parsed = Parse(input, selectedProfile);
            }
            catch (Exception ex)
                when (ex is FormatException
                    or ArgumentException)
            {
                SetResult(
                    "INVALID INPUT",
                    ex.Message,
                    "Fuel plan —",
                    "Runway selection —",
                    []);
                return;
            }

            if (useAcceptedReservation
                && !MatchesAcceptedContext(
                    parsed,
                    out string mismatchDetail))
            {
                SetResult(
                    "ACCEPTED PLAN MISMATCH",
                    mismatchDetail,
                    "Fuel plan —",
                    "Runway selection —",
                    []);
                return;
            }

            CompositeDispatchPlanningResult result =
                useAcceptedReservation
                    ? await _planning
                        .EvaluateAsync(
                            _selectedAircraftId,
                            parsed.OriginIcao,
                            parsed.DestinationIcao,
                            parsed.Cruise,
                            parsed.RouteFuel,
                            parsed.Requirements,
                            _acceptedReservationId!,
                            cancellationToken)
                        .ConfigureAwait(true)
                    : await _planning
                        .EvaluateAsync(
                            _selectedAircraftId,
                            parsed.OriginIcao,
                            parsed.DestinationIcao,
                            parsed.Cruise,
                            parsed.RouteFuel,
                            parsed.Requirements,
                            cancellationToken)
                        .ConfigureAwait(true);

            ApplyResult(
                result,
                parsed.Requirements.WeatherLimits is not null);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyAcceptedContext(
        AcceptedDispatchPlanningContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        SetField(
            ref _acceptedContextText,
            context.Detail,
            nameof(AcceptedContextText));
        SetField(
            ref _acceptedOriginIcao,
            context.OriginIcao,
            nameof(AcceptedOriginIcao));
        SetField(
            ref _acceptedDestinationIcao,
            context.DestinationIcao,
            nameof(AcceptedDestinationIcao));
        SetField(
            ref _acceptedPayloadPoundsText,
            FormatOptionalInput(context.PayloadPounds),
            nameof(AcceptedPayloadPoundsText));
        SetField(
            ref _acceptedRouteDistanceText,
            FormatOptionalInput(context.RouteDistanceNauticalMiles),
            nameof(AcceptedRouteDistanceText));

        _acceptedAircraftId =
            context.Status == AcceptedDispatchPlanningContextStatus.Ready
                ? context.CanonicalAircraftId
                : null;
        _acceptedReservationId =
            context.Status == AcceptedDispatchPlanningContextStatus.Ready
                ? context.ReservationId
                : null;
        _acceptedPayloadPounds = context.PayloadPounds;
        _acceptedRouteDistanceNauticalMiles =
            context.RouteDistanceNauticalMiles;
        SetField(
            ref _isAcceptedContextLocked,
            _acceptedReservationId is not null,
            nameof(IsAcceptedContextLocked));
    }

    private bool MatchesAcceptedContext(
        ParsedDispatchInput parsed,
        out string detail)
    {
        if (_acceptedOriginIcao is null
            || _acceptedDestinationIcao is null
            || _acceptedPayloadPounds is not { } acceptedPayload
            || _acceptedRouteDistanceNauticalMiles is not { } acceptedDistance)
        {
            detail =
                "The accepted contract has no complete authoritative payload and route-distance terms. Its Fleet reservation cannot prepare a dispatch plan.";
            return false;
        }

        if (!string.Equals(
                parsed.OriginIcao,
                _acceptedOriginIcao,
                StringComparison.Ordinal)
            || !string.Equals(
                parsed.DestinationIcao,
                _acceptedDestinationIcao,
                StringComparison.Ordinal)
            || Math.Abs(
                parsed.Requirements.PayloadPounds
                - acceptedPayload) > 1e-9
            || Math.Abs(
                parsed.RouteFuel.TotalRouteDistanceNauticalMiles
                - acceptedDistance) > 1e-9)
        {
            detail =
                "The supplied origin, destination, payload, or route distance does not match the immutable accepted contract. Its Fleet reservation was not used.";
            return false;
        }

        detail = string.Empty;
        return true;
    }

    private void ApplyResult(
        CompositeDispatchPlanningResult result,
        bool weatherLimitsApplied)
    {
        if (result.Status == CompositeDispatchPlanningStatus.InsufficientData)
        {
            string reasons = string.Join(
                " · ",
                new object?[]
                {
                    result.Reason,
                    result.FuelWeightReason,
                    result.RouteFuelReason,
                    result.CruisePerformanceReason
                }
                .Where(static reason => reason is not null)
                .Select(static reason => FormatWords(reason!.ToString()!)));

            SetResult(
                "INSUFFICIENT DATA",
                reasons.Length == 0
                    ? "The composite planner did not return enough authoritative data."
                    : reasons,
                FormatFuel(result),
                "Runway selection —",
                []);
            return;
        }

        DispatchFeasibilityResult dispatch = result.DispatchResult
            ?? throw new InvalidDataException(
                "An evaluated composite dispatch result must contain physical feasibility evidence.");

        string status = dispatch.Status switch
        {
            DispatchFeasibilityStatus.Feasible => "FEASIBLE",
            DispatchFeasibilityStatus.Infeasible => "INFEASIBLE",
            DispatchFeasibilityStatus.InsufficientData => "INSUFFICIENT DATA",
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };

        DispatchIssueViewModel[] issues = dispatch.Issues
            .Select(static issue => new DispatchIssueViewModel(
                FormatWords(issue.Reason.ToString()),
                FormatIssueDetail(issue)))
            .ToArray();

        SetResult(
            status,
            dispatch.Status == DispatchFeasibilityStatus.Feasible
                ? weatherLimitsApplied
                    ? "Fuel, weight, range, airport, runway and requested weather-limit checks passed for the supplied plan."
                    : "Fuel, weight, range, airport and runway checks passed for the supplied plan. Weather was not evaluated because no weather limits were supplied."
                : "The supplied plan is not dispatch-ready. Review every reported issue; unknown data never passes as feasible.",
            FormatFuel(result),
            FormatRunways(dispatch),
            issues);
    }

    private static ParsedDispatchInput Parse(
        DispatchPlanningFormInput input,
        DispatchCruiseProfileOptionViewModel selectedProfile)
    {
        string origin = ParseIcao(input.OriginIcao, "Origin ICAO");
        string destination = ParseIcao(input.DestinationIcao, "Destination ICAO");
        double totalDistance = ParseDouble(
            input.TotalRouteDistanceNauticalMiles,
            "Total route distance");

        double plannedCruiseWeight = ParseDouble(
            input.PlannedCruiseWeightPounds,
            "Planned cruise weight");
        double isaDeviation = ParseDouble(
            input.IsaDeviationCelsius,
            "ISA deviation");
        double pressureAltitude = ParseDouble(
            input.PressureAltitudeFeet,
            "Pressure altitude");

        if (!selectedProfile.Envelope.Contains(
                plannedCruiseWeight,
                isaDeviation,
                pressureAltitude))
        {
            throw new ArgumentException(
                "Cruise weight, ISA deviation and pressure altitude must be inside the selected profile's displayed common envelope.");
        }

        var cruise = new CruisePerformancePlanningRequest(
            selectedProfile.ProfileIndex,
            selectedProfile.FuelTypeIndex,
            plannedCruiseWeight,
            isaDeviation,
            pressureAltitude);

        var routeFuel = new RouteFuelPlanningRequest(
            totalDistance,
            ParseDouble(input.CruiseSegmentDistanceNauticalMiles, "Cruise segment distance"),
            ParseDouble(input.ClimbTimeMinutes, "Climb time"),
            ParseDouble(input.DescentTimeMinutes, "Descent time"),
            ParseDouble(input.ClimbFuelGallons, "Climb fuel"),
            ParseDouble(input.DescentFuelGallons, "Descent fuel"),
            new(
                ParseDouble(input.ContingencyPercent, "Contingency fuel percentage"),
                ParseDouble(input.FinalReserveMinutes, "Final reserve minutes"),
                ParseDouble(input.AdditionalReserveFuelGallons, "Additional reserve fuel")));

        DispatchWeatherLimits? weatherLimits = input.ApplyWeatherLimits
            ? new(
                ParseDouble(input.MaximumCrosswindKnots, "Maximum crosswind"),
                ParseDouble(input.MaximumTailwindKnots, "Maximum tailwind"),
                ParseOptionalDouble(input.MaximumDensityAltitudeFeet, "Maximum density altitude"),
                ParseOptionalDouble(input.MinimumVisibilityStatuteMiles, "Minimum visibility"),
                ParseOptionalDouble(input.MinimumCeilingFeetAgl, "Minimum ceiling"))
            : null;

        var requirements = new OperationDispatchRequirements(
            ParseDouble(input.PayloadPounds, "Payload"),
            totalDistance,
            RangeSafetyMarginPercent:
                ParseDouble(input.RangeSafetyMarginPercent, "Range safety margin"),
            RunwayLengthSafetyMarginPercent:
                ParseDouble(input.RunwaySafetyMarginPercent, "Runway safety margin"),
            WeatherLimits: weatherLimits);

        cruise.Validate();
        routeFuel.Validate();
        requirements.Validate();
        return new(origin, destination, cruise, routeFuel, requirements);
    }

    private static string ParseIcao(string value, string label)
    {
        string normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length != 4
            || normalized.Any(static character => character is < 'A' or > 'Z'))
        {
            throw new FormatException($"{label} must contain exactly four letters.");
        }

        return normalized;
    }

    private static double ParseDouble(string value, string label)
    {
        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out double result)
            || !double.IsFinite(result))
        {
            throw new FormatException($"{label} must be a finite number.");
        }

        return result;
    }

    private static double? ParseOptionalDouble(string value, string label) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : ParseDouble(value, label);

    private static string? FormatOptionalInput(double? value) =>
        value?.ToString("R", CultureInfo.CurrentCulture);

    private static string FormatFuel(CompositeDispatchPlanningResult result)
    {
        if (result.FuelWeightPlan is not { } plan)
            return "Fuel plan unavailable";

        return string.Create(
            CultureInfo.CurrentCulture,
            $"Required {plan.RequiredFuelGallons:0.0} gal / {plan.RequiredFuelPounds:0} lb · fuel type index {plan.FuelTypeIndex}");
    }

    private static string FormatRunways(DispatchFeasibilityResult result) =>
        $"Origin runway {result.OriginRunwayIdentifier ?? "unknown"} · "
        + $"destination runway {result.DestinationRunwayIdentifier ?? "unknown"}";

    private static string FormatIssueDetail(DispatchFeasibilityIssue issue)
    {
        var details = new List<string>();
        if (issue.Endpoint is { } endpoint)
            details.Add(FormatWords(endpoint.ToString()));
        if (!string.IsNullOrWhiteSpace(issue.AirportIcao))
            details.Add(issue.AirportIcao);
        if (!string.IsNullOrWhiteSpace(issue.RunwayIdentifier))
            details.Add($"runway {issue.RunwayIdentifier}");
        if (issue.RequiredFeet is { } requiredFeet)
            details.Add($"required {requiredFeet:0} ft");
        if (issue.AvailableFeet is { } availableFeet)
            details.Add($"available {availableFeet:0} ft");
        if (issue.RequiredPounds is { } requiredPounds)
            details.Add($"required {requiredPounds:0} lb");
        if (issue.AvailablePounds is { } availablePounds)
            details.Add($"available {availablePounds:0} lb");
        if (issue.RequiredNauticalMiles is { } requiredNm)
            details.Add($"required {requiredNm:0} nm");
        if (issue.AvailableNauticalMiles is { } availableNm)
            details.Add($"available {availableNm:0} nm");
        if (issue.ObservedKnots is { } observedKnots)
            details.Add($"observed {observedKnots:0.0} kt");
        if (issue.LimitKnots is { } limitKnots)
            details.Add($"limit {limitKnots:0.0} kt");
        if (issue.ObservedDensityAltitudeFeet is { } densityAltitude)
            details.Add($"observed density altitude {densityAltitude:0} ft");
        if (issue.MaximumDensityAltitudeFeet is { } maximumDensityAltitude)
            details.Add($"maximum density altitude {maximumDensityAltitude:0} ft");
        if (issue.ObservedVisibilityStatuteMiles is { } visibility)
            details.Add($"observed visibility {visibility:0.0} sm");
        if (issue.MinimumVisibilityStatuteMiles is { } minimumVisibility)
            details.Add($"minimum visibility {minimumVisibility:0.0} sm");
        if (issue.ObservedCeilingFeetAgl is { } ceiling)
            details.Add($"observed ceiling {ceiling:0} ft AGL");
        if (issue.MinimumCeilingFeetAgl is { } minimumCeiling)
            details.Add($"minimum ceiling {minimumCeiling:0} ft AGL");

        return details.Count == 0
            ? "No additional quantitative evidence was supplied."
            : string.Join(" · ", details);
    }

    private static string FormatWords(string value) =>
        string.Concat(
            value.Select((character, index) =>
                index > 0 && char.IsUpper(character)
                    ? $" {character}"
                    : character.ToString()));

    private void SetResult(
        string status,
        string detail,
        string fuel,
        string runways,
        IReadOnlyList<DispatchIssueViewModel> issues)
    {
        SetField(ref _resultStatus, status, nameof(ResultStatus));
        SetField(ref _resultDetail, detail, nameof(ResultDetail));
        SetField(ref _fuelSummary, fuel, nameof(FuelSummary));
        SetField(ref _runwaySummary, runways, nameof(RunwaySummary));
        SetField(ref _issues, issues, nameof(Issues));
    }

    private void SetBusy(bool value)
    {
        SetField(ref _isBusy, value, nameof(IsBusy));
        OnPropertyChanged(nameof(CanEvaluate));
        OnPropertyChanged(nameof(EvaluateButtonText));
    }

    private bool SetField<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));

    private sealed record ParsedDispatchInput(
        string OriginIcao,
        string DestinationIcao,
        CruisePerformancePlanningRequest Cruise,
        RouteFuelPlanningRequest RouteFuel,
        OperationDispatchRequirements Requirements);
}

public sealed record DispatchAircraftOptionViewModel(
    string AircraftId,
    string DisplayName);

public sealed record DispatchCruiseProfileOptionViewModel(
    int ProfileIndex,
    int FuelTypeIndex,
    string DisplayName,
    DispatchCruiseProfileEnvelope Envelope,
    string EvidenceDetail);

public sealed record DispatchIssueViewModel(
    string Reason,
    string Detail);
