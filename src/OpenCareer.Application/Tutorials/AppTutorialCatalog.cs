namespace OpenCareer.Application.Tutorials;

public sealed class AppTutorialCatalog : ITutorialCatalog
{
    public const string AppIntroId = "app-intro";
    public const string FirstJobId = "first-job";

    private readonly IReadOnlyDictionary<string, TutorialDefinition> _definitions =
        new Dictionary<string, TutorialDefinition>(StringComparer.Ordinal)
        {
            [AppIntroId] = CreateAppIntro(),
            [FirstJobId] = CreateFirstJob()
        };

    public TutorialDefinition? Get(string tutorialId) =>
        _definitions.TryGetValue(tutorialId, out var definition) ? definition : null;

    private static TutorialDefinition CreateAppIntro()
    {
        TutorialStep[] steps =
        [
            Step("welcome", "Welcome to OpenCareer",
                "OpenCareer adds a persistent single-player career, jobs, operations, economy and progression around Microsoft Flight Simulator 2024. MSFS flies the aircraft; OpenCareer owns the career.",
                null, "shell"),
            Step("connection", "Simulator connection",
                "OpenCareer can stay open without MSFS. Waiting means the app is healthy and will connect automatically. A simulator disconnect never turns live telemetry into a completed career flight.",
                "dashboard", "shell"),
            Step("dashboard", "Home / Dashboard",
                "Home answers what matters now: simulator status, location, aircraft access, active work, important alerts and the next useful action.",
                "dashboard", "dashboard"),
            Step("career-loop", "The normal career loop",
                "Find suitable work, validate aircraft and route feasibility, accept, prepare, fly, land, park and shut down when required, validate objectives, settle once, then write the logbook.",
                "dashboard", "career-loop"),
            Step("dispatch", "Dispatch",
                "Dispatch checks the operation before launch: aircraft, payload, fuel, route, runway feasibility, weather inputs and mission constraints. Invalid work must explain why it is blocked.",
                "dispatch", "dispatch"),
            Step("jobs", "Jobs",
                "Jobs are filtered by location, qualifications, aircraft access, capability, market state and mission rules. Browsing creates no commitment; accepting work does.",
                "jobs", "jobs"),
            Step("current-flight", "Current Flight",
                "Current Flight is the live workspace for checklist, flight status, mission objectives and events. Telemetry is normalized before it reaches the career logic.",
                "current-flight", "current-flight"),
            Step("world", "Map / World",
                "The world view will combine airports, bases, routes, opportunities, events and market overlays while keeping real geographic data distinct from OpenCareer simulation.",
                "world", "world"),
            Step("aircraft", "Aircraft",
                "Installed, employer, rented, assigned and owned aircraft are different forms of access. An aircraft installed in MSFS is not automatically owned in your career.",
                "aircraft", "aircraft"),
            Step("bases", "Hangar / Bases",
                "Bases control physical geography: home airport, storage, services, aircraft location and expansion. OpenCareer does not teleport your company or fleet.",
                "bases", "bases"),
            Step("maintenance", "Maintenance",
                "Maintenance tracks condition, service, incidents, parts, downtime and history. OpenCareer only uses aircraft-specific simulator state when it can be trusted.",
                "maintenance", "maintenance"),
            Step("company", "Company",
                "Company play is optional. You can remain an employee, or later manage routes, contracts, bases, aircraft utilization and staff.",
                "company", "company"),
            Step("finances", "Finances",
                "Finances will show cash, income, expenses, loans, insurance, maintenance reserves and transactions. Quotes are not authoritative transactions until persisted atomically.",
                "finances", "finances"),
            Step("markets", "Markets",
                "Markets model regional demand, supply, commodities and operating pressure. OpenCareer-generated values are game simulation unless explicitly labeled as external data.",
                "markets", "markets"),
            Step("military", "Military / Government",
                "Government and military access is earned separately from civilian ownership. MSFS supplies flight telemetry; OpenCareer owns special-operation objectives and any later simulated conflict state.",
                "military", "military"),
            Step("logbook", "Logbook",
                "The logbook will preserve trustworthy flight sessions, legs, times, takeoffs, landings, route evidence, incidents, mission outcome and settlement.",
                "logbook", "logbook"),
            Step("career", "Career",
                "Progression centers on licenses, ratings, experience, recency, reputation, relationships and qualifications rather than arbitrary XP grinding.",
                "career", "career"),
            Step("settings", "Settings",
                "Settings controls simulator diagnostics, units, preferences, recovery information and tutorials. You can restart this introduction here at any time.",
                "settings", "settings"),
            Step("first-operation", "Your first operation",
                "When the playable job loop is enabled: choose feasible work, review dispatch, accept, load the correct aircraft, follow preparation and mission requirements, then park, shut down, debrief and settle once.",
                "dashboard", "first-job"),
            Step("finish", "You know where everything lives",
                "Home shows what to do next. Jobs and Dispatch find work. Current Flight runs the operation. Aircraft, Bases and Maintenance manage capability. Finances, Markets and Company manage the business. Logbook and Career preserve progression.",
                "dashboard", "shell")
        ];

        return new TutorialDefinition(AppIntroId, 1, steps);
    }

    private static TutorialDefinition CreateFirstJob()
    {
        TutorialStep[] steps =
        [
            Step("job-find", "Find suitable work",
                "Start with a job that matches your location, qualifications, available aircraft and intended session length.",
                "jobs", "jobs"),
            Step("job-eligibility", "Read eligibility",
                "A locked job should explain exactly what is missing: qualification, aircraft access, runway capability, location or another requirement.",
                "jobs", "jobs"),
            Step("job-dispatch", "Review dispatch",
                "Check route, aircraft, payload, fuel, runway feasibility and mission constraints before committing.",
                "dispatch", "dispatch"),
            Step("job-accept", "Accept the operation",
                "Acceptance creates the career commitment. Loading an aircraft in MSFS by itself never creates a paid job.",
                "jobs", "jobs"),
            Step("job-prepare", "Prepare the aircraft",
                "Use the live checklist for servicing, loading, startup and taxi requirements. Steps auto-complete only when trustworthy evidence proves them.",
                "current-flight", "current-flight"),
            Step("job-fly", "Fly the mission",
                "Follow the route and mission-specific objectives. Go-arounds and legitimate diversions are evaluated by context rather than treated as automatic failures.",
                "current-flight", "current-flight"),
            Step("job-arrive", "Finish the operation",
                "A landing alone is normally not enough. Conventional jobs require the mission-defined arrival state, usually taxi, parking, unloading or servicing, and shutdown.",
                "current-flight", "current-flight"),
            Step("job-debrief", "Review the debrief",
                "Review flight evidence, mission outcome, incidents, assistance flags and any settlement before the operation is written to history.",
                "logbook", "logbook")
        ];

        return new TutorialDefinition(FirstJobId, 1, steps);
    }

    private static TutorialStep Step(
        string id,
        string title,
        string body,
        string? navigationTag,
        string featureKey) =>
        new(id, 1, title, body, navigationTag, null, featureKey);
}
