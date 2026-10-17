using System.Globalization;
using OpenCareer.Domain.Conflict;

namespace OpenCareer.Application.Military;

public enum ConflictCommunicationChannel
{
    Command,
    Dispatch,
    Intelligence,
    Flight
}

public enum ConflictCommunicationPriority
{
    Routine,
    Advisory,
    Priority,
    Immediate
}

public sealed record ConflictCommunicationEntry(
    string CommunicationId,
    DateTimeOffset Timestamp,
    ConflictCommunicationChannel Channel,
    ConflictCommunicationPriority Priority,
    string Message)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CommunicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Message);

        if (!Enum.IsDefined(Channel))
            throw new ArgumentOutOfRangeException(nameof(Channel));

        if (!Enum.IsDefined(Priority))
            throw new ArgumentOutOfRangeException(nameof(Priority));
    }
}

public static class ConflictCommunicationsBuilder
{
    private const int MaximumSupportMessages = 5;
    private const int MaximumThreatMessages = 3;

    public static ConflictCommunicationEntry[] Build(
        ConflictOperationsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var entries = new List<ConflictCommunicationEntry>(
            2 + MaximumSupportMessages + MaximumThreatMessages);

        entries.Add(BuildCampaignStatus(snapshot));

        if (snapshot.ActiveOperation is { } active)
        {
            entries.Add(
                new ConflictCommunicationEntry(
                    $"flight:{active.MissionId:N}:{active.Stage}",
                    snapshot.AsOf,
                    ConflictCommunicationChannel.Flight,
                    ConflictCommunicationPriority.Priority,
                    $"Assigned {Words(active.Type.ToString())} operation is {Words(active.Stage).ToLowerInvariant()}."));
        }

        entries.AddRange(
            snapshot.SupportRequests
                .OrderByDescending(static request => request.Urgency)
                .ThenBy(static request => request.Type)
                .ThenBy(static request => request.RequestId, StringComparer.Ordinal)
                .Take(MaximumSupportMessages)
                .Select(request =>
                    new ConflictCommunicationEntry(
                        $"dispatch:{request.RequestId}:{request.Status}",
                        snapshot.AsOf,
                        ConflictCommunicationChannel.Dispatch,
                        SupportPriority(request.Urgency),
                        $"{Words(request.Type.ToString())} request is {Words(request.Status.ToString()).ToLowerInvariant()} " +
                        $"with {Words(request.Urgency.ToString()).ToLowerInvariant()} urgency; " +
                        $"window closes {request.ExpiresAt.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture)} UTC.")));

        entries.AddRange(
            snapshot.Threats
                .OrderByDescending(static threat => threat.Severity)
                .ThenBy(static threat => threat.ThreatId)
                .Take(MaximumThreatMessages)
                .Select(threat =>
                    new ConflictCommunicationEntry(
                        $"intel:{threat.ThreatId:N}",
                        snapshot.AsOf,
                        ConflictCommunicationChannel.Intelligence,
                        ThreatPriority(threat.Severity),
                        $"{Words(threat.Type.ToString())} threat active; " +
                        $"{threat.RadiusNauticalMiles.ToString("0", CultureInfo.InvariantCulture)} NM envelope, " +
                        $"severity {threat.Severity.ToString("P0", CultureInfo.InvariantCulture)}.")));

        return entries
            .OrderByDescending(static entry => entry.Priority)
            .ThenBy(static entry => ChannelOrder(entry.Channel))
            .ThenBy(static entry => entry.CommunicationId, StringComparer.Ordinal)
            .ToArray();
    }

    private static ConflictCommunicationEntry BuildCampaignStatus(
        ConflictOperationsSnapshot snapshot)
    {
        if (snapshot.Outcome != ConflictCampaignOutcome.Ongoing)
        {
            return new ConflictCommunicationEntry(
                $"command:{snapshot.CampaignId}:outcome:{snapshot.Outcome}",
                snapshot.AsOf,
                ConflictCommunicationChannel.Command,
                ConflictCommunicationPriority.Priority,
                $"{snapshot.OperationName} concluded with {Words(snapshot.Outcome.ToString()).ToLowerInvariant()}.");
        }

        ConflictCommunicationPriority priority =
            Math.Abs(snapshot.FriendlyMomentum) >= 0.50
                ? ConflictCommunicationPriority.Advisory
                : ConflictCommunicationPriority.Routine;

        return new ConflictCommunicationEntry(
            $"command:{snapshot.CampaignId}:state:{snapshot.Phase}",
            snapshot.AsOf,
            ConflictCommunicationChannel.Command,
            priority,
            $"{snapshot.OperationName} is in {Words(snapshot.Phase.ToString()).ToLowerInvariant()}; " +
            $"friendly control {snapshot.FriendlyControlAverage.ToString("P0", CultureInfo.InvariantCulture)}, " +
            $"momentum {snapshot.FriendlyMomentum.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)}.");
    }

    private static ConflictCommunicationPriority SupportPriority(
        SupportUrgency urgency) =>
        urgency switch
        {
            SupportUrgency.Immediate =>
                ConflictCommunicationPriority.Immediate,
            SupportUrgency.Priority =>
                ConflictCommunicationPriority.Priority,
            SupportUrgency.Routine =>
                ConflictCommunicationPriority.Advisory,
            _ => throw new ArgumentOutOfRangeException(nameof(urgency))
        };

    private static ConflictCommunicationPriority ThreatPriority(
        double severity)
    {
        if (!double.IsFinite(severity) || severity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(severity));

        if (severity >= 0.75)
            return ConflictCommunicationPriority.Immediate;

        if (severity >= 0.50)
            return ConflictCommunicationPriority.Priority;

        return ConflictCommunicationPriority.Advisory;
    }

    private static int ChannelOrder(
        ConflictCommunicationChannel channel) =>
        channel switch
        {
            ConflictCommunicationChannel.Command => 0,
            ConflictCommunicationChannel.Flight => 1,
            ConflictCommunicationChannel.Dispatch => 2,
            ConflictCommunicationChannel.Intelligence => 3,
            _ => 4
        };

    private static string Words(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var result = new System.Text.StringBuilder(value.Length + 8);

        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];

            if (index > 0
                && char.IsUpper(current)
                && !char.IsUpper(value[index - 1]))
            {
                result.Append(' ');
            }

            result.Append(current);
        }

        return result.ToString();
    }
}
