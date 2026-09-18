namespace OpenCareer.App.ViewModels;

public sealed class MilitaryOperationsViewModel
{
    public string AuthorizationStatus => "Authorization not configured";

    public string AuthorizationDetail =>
        "Military and government work remains locked until a career authorization profile is earned or assigned.";

    public string ActiveOperationStatus => "No active military operation";

    public string ActiveOperationDetail =>
        "OpenCareer will show mission objectives, threat state and recovery requirements here after a military assignment is accepted.";

    public string ConflictSimulationStatus => "Conflict simulation ready";

    public string ConflictSimulationDetail =>
        "Regional escalation, active-conflict, ceasefire and recovery states are deterministic OpenCareer simulation.";

    public string ThreatSimulationStatus => "Threat simulation ready";

    public string ThreatSimulationDetail =>
        "Abstract simulated threat zones can produce bounded exposure and mission-risk values without relying on real weapon-performance data.";

    public string MissionFlowStatus => "Mission flow ready";

    public string MissionFlowDetail =>
        "Briefed → accepted → preflight → en route → on station → objective → egress → recovery → complete.";

    public string EngagementSimulationStatus => "Engagement resolver ready";

    public string EngagementSimulationDetail =>
        "Mission effects are deterministic and bounded. No native MSFS weapon, target, hit or damage events are assumed.";

    public string MissionFamilies =>
        "Training • Readiness • Patrol • Surveillance • Transport • Aeromedical Evacuation • Search & Rescue • Tanker Support • Escort • Intercept • Airfield Reinforcement • Air Support";
}
