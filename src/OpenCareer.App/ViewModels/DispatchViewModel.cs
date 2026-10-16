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
    string ProfileIndex,
    string FuelTypeIndex,
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
    string RunwaySafetyMarginPercent);

public sealed class DispatchViewModel : INotifyPropertyChanged
{
    private readonly IInstalledAircraftInventorySource _aircraftInventory;
    private readonly ICompositeDispatchPlanningService _planning;
    private readonly IAcceptedDispatchPlanningContextSource _acceptedContext;
    private IReadOnlyList<DispatchAircraftOptionViewModel> _aircraftOptions =
        Array.Empty<DispatchAircraftOptionViewModel>();
    private string? _selectedAircraftId;
    private string _inventoryStatus = "Installed aircraft have not been loaded.";
    private string _acceptedContextText =
        "Accepted-job dispatch context has not been loaded.";
    private string? _acceptedOriginIcao;
    private string? _acceptedDestinationIcao;
    private string? _acceptedAircraftId;
    private string? _acceptedReservationId;
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
        ICompositeDispatchPlanningService planning,
        IAcceptedDispatchPlanningContextSource acceptedContext)
    {
        _aircraftInventory = aircraftInventory
            ?? throw new ArgumentNullException(nameof(aircraftInventory));
        _planning = planning
            ?? throw new ArgumentNullException(nameof(planning));
        _acceptedContext = acceptedContext
            ?? throw new ArgumentNullException(nameof(acceptedContext));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<DispatchAircraftOptionViewModel> AircraftOptions => _aircraftOptions;
    public string? SelectedAircraftId => _selectedAircraftId;
    public string InventoryStatus => _inventoryStatus;
    public string AcceptedContextText => _acceptedContextText;
    public string? AcceptedOriginIcao => _acceptedOriginIcao;
    public string? AcceptedDestinationIcao => _acceptedDestinationIcao;
    public string ResultStatus => _resultStatus;
    public string ResultDetail => _resultDetail;
    public string FuelSummary => _fuelSummary;
    public string RunwaySummary => _runwaySummary;
    public IReadOnlyList<DispatchIssueViewModel> Issues => _issues;
    public bool IsBusy => _isBusy;
    public bool CanEvaluate => !_isBusy && _selectedAircraftId is not null;
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
        SetSelectedAircraft(selection);

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

    public void SetSelectedAircraft(string? aircraftId)
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

        if (SetField(
                ref _selectedAircraftId,
                normalized,
                nameof(SelectedAircraftId)))
        {
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

        SetBusy(true);
        try
        {
            ParsedDispatchInput parsed;
            try
            {
                parsed = Parse(input);
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

            bool useAcceptedReservation =
                _acceptedReservationId is not null
                && string.Equals(
                    _selectedAircraftId,
                    _acceptedAircraftId,
                    StringComparison.OrdinalIgnoreCase);

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

            ApplyResult(result);
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

        _acceptedAircraftId =
            context.Status == AcceptedDispatchPlanningContextStatus.Ready
                ? context.CanonicalAircraftId
                : null;
        _acceptedReservationId =
            context.Status == AcceptedDispatchPlanningContextStatus.Ready
                ? context.ReservationId
                : null;
    }

    private void ApplyResult(CompositeDispatchPlanningResult result)
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
                ? "Fuel, weight, range, airport and runway checks passed for the supplied plan. Weather was not evaluated because no weather limits were supplied."
                : "The supplied plan is not dispatch-ready. Review every reported issue; unknown data never passes as feasible.",
            FormatFuel(result),
            FormatRunways(dispatch),
            issues);
    }

    private static ParsedDispatchInput Parse(DispatchPlanningFormInput input)
    {
        string origin = ParseIcao(input.OriginIcao, "Origin ICAO");
        string destination = ParseIcao(input.DestinationIcao, "Destination ICAO");
        double totalDistance = ParseDouble(
            input.TotalRouteDistanceNauticalMiles,
            "Total route distance");

        var cruise = new CruisePerformancePlanningRequest(
            ParseInt(input.ProfileIndex, "Cruise profile index"),
            ParseInt(input.FuelTypeIndex, "Fuel type index"),
            ParseDouble(input.PlannedCruiseWeightPounds, "Planned cruise weight"),
            ParseDouble(input.IsaDeviationCelsius, "ISA deviation"),
            ParseDouble(input.PressureAltitudeFeet, "Pressure altitude"));

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

        var requirements = new OperationDispatchRequirements(
            ParseDouble(input.PayloadPounds, "Payload"),
            totalDistance,
            RangeSafetyMarginPercent:
                ParseDouble(input.RangeSafetyMarginPercent, "Range safety margin"),
            RunwayLengthSafetyMarginPercent:
                ParseDouble(input.RunwaySafetyMarginPercent, "Runway safety margin"));

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

    private static int ParseInt(string value, string label)
    {
        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out int result))
        {
            throw new FormatException($"{label} must be a whole number.");
        }

        return result;
    }

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

public sealed record DispatchIssueViewModel(
    string Reason,
    string Detail);
