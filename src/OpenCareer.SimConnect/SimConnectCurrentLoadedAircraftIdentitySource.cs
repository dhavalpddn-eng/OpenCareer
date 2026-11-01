using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.SimConnect;

public sealed class SimConnectCurrentLoadedAircraftIdentitySource(
    SimConnectConnection connection)
    : ICurrentLoadedAircraftIdentitySource
{
    private readonly SimConnectConnection _connection =
        connection
        ?? throw new ArgumentNullException(nameof(connection));

    public CurrentLoadedAircraftIdentitySnapshot Current
    {
        get
        {
            SimulatorConnectionSnapshot connection =
                _connection.Current;

            string? currentAircraftTitle =
                _connection.CurrentAircraftTitle;

            if (connection.State
                    != SimulatorConnectionState.Connected
                || string.IsNullOrWhiteSpace(
                    currentAircraftTitle))
            {
                return CurrentLoadedAircraftIdentitySnapshot.Unavailable;
            }

            if (_connection.Current.State
                    != SimulatorConnectionState.Connected
                || !string.Equals(
                    currentAircraftTitle,
                    _connection.CurrentAircraftTitle,
                    StringComparison.Ordinal))
            {
                return CurrentLoadedAircraftIdentitySnapshot.Unavailable;
            }

            string canonicalAircraftId =
                AircraftCanonicalIdentity.FromMsfsTitle(
                    MsfsAircraftTitleCanonicalizer.Resolve(
                        currentAircraftTitle));

            return CurrentLoadedAircraftIdentitySnapshot
                .Identified(canonicalAircraftId);
        }
    }
}
