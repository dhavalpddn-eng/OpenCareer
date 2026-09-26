using System.Reflection;
using System.Runtime.InteropServices;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Simulator;
using OpenCareer.SimConnect;
using OpenCareer.SimConnect.Native;

namespace OpenCareer.Tests;

public sealed partial class SimConnectFailureActuatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisconnectDropsQueuedOrSentCommandAndReconnectRequiresFreshEvidence(bool sent)
    {
        await using var h = new Harness();
        await h.StartAsync(false);
        using var release = new ManualResetEventSlim();
        Task<SimulatorFailureActuationResult> pending;
        if (sent)
        {
            pending = h.Actuator.EnsureEngineFailedAsync(1);
            await Until(() => h.Api.Transmissions.Count == 1);
            h.Api.Enqueue(SimConnectPackets.Header(3));
        }
        else
        {
            // The worker receives Quit while the application fills its one queued command slot.
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            h.Api.Enqueue(SimConnectPackets.Header(3), action: () => { entered.SetResult(); release.Wait(TimeSpan.FromSeconds(5)); });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            pending = h.Actuator.EnsureEngineFailedAsync(1);
            release.Set();
        }
        Assert.Equal(SimulatorFailureActuationStatus.SimulatorUnavailable, (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        Assert.False(h.Connection.FailureState.IsAvailable);
        await Until(() => h.Api.Attempts == 2 && h.Connection.Current.State == SimulatorConnectionState.Connecting);
        await h.PacketAsync(SimConnectPackets.Open());
        Assert.Equal(SimulatorConnectionState.Connected, h.Connection.Current.State);
        Assert.Equal(2, h.Api.EventMappings.Count);
        Assert.Equal(2, h.Api.TelemetryRequests.Count(r => r.DefinitionId == SimConnectEngineFailureDefinition.DefinitionId));
        await h.AssertUnavailableAsync();
        await h.ObserveAsync(false);
        await h.DrainAsync();
        Assert.Equal(sent ? 1 : 0, h.Api.Transmissions.Count); // Fresh healthy readback must not replay old work.
        await h.ObserveAsync(true);
        Assert.Equal(SimulatorFailureActuationStatus.AlreadyFailed, (await h.Actuator.EnsureEngineFailedAsync(1)).Status);
        Assert.Equal(sent ? 1 : 0, h.Api.Transmissions.Count);
        Assert.False(h.Api.OpenedBeforeClose);
        Assert.False(h.Api.OverlapDetected);
        Assert.Single(h.Api.ThreadIds);
    }

    [Fact]
    public async Task StopAndDisposeCompletePendingCommandWithoutReplayOrTaskLeak()
    {
        await using var h = new Harness();
        await h.StartAsync(false);
        var pending = h.Actuator.EnsureEngineFailedAsync(1);
        await Until(() => h.Api.Transmissions.Count == 1);
        await h.Connection.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SimulatorFailureActuationStatus.SimulatorUnavailable, (await pending).Status);
        await h.AssertUnavailableAsync();
        await h.StartAsync(true);
        Assert.Equal(SimulatorFailureActuationStatus.AlreadyFailed, (await h.Actuator.EnsureEngineFailedAsync(1)).Status);
        await h.Connection.DisposeAsync();
        await h.AssertUnavailableAsync();
        Assert.Single(h.Api.Transmissions);
    }

    [Fact]
    public async Task CallerCancellationBeforeSendDropsQueueWithoutNativeTransmission()
    {
        await using var h = new Harness();
        await h.StartAsync(false);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Api.Enqueue(action: () => { entered.SetResult(); release.Wait(TimeSpan.FromSeconds(5)); });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();
        var pending = h.Actuator.EnsureEngineFailedAsync(1, cts.Token);
        cts.Cancel();
        release.Set();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        await h.DrainAsync();
        Assert.Empty(h.Api.Transmissions);
        await h.CoreTelemetryAsync();
    }

    [Fact]
    public async Task CallerCancellationAfterSendPreservesUncertaintyAndNeverSendsInverseToggle()
    {
        await using var h = new Harness();
        await h.StartAsync(false);
        using var cts = new CancellationTokenSource();
        var pending = h.Actuator.EnsureEngineFailedAsync(1, cts.Token);
        await Until(() => h.Api.Transmissions.Count == 1);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        await h.DrainAsync();
        Assert.Equal(SimulatorFailureActuationStatus.AcknowledgementTimeout, (await h.Actuator.EnsureEngineFailedAsync(1)).Status);
        await h.ObserveAsync(true);
        Assert.Equal(SimulatorFailureActuationStatus.AlreadyFailed, (await h.Actuator.EnsureEngineFailedAsync(1)).Status);
        Assert.Single(h.Api.Transmissions);
    }

    [Fact]
    public async Task PreCancelledCallerCannotQueueOrTransmit()
    {
        await using var h = new Harness();
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Actuator.EnsureEngineFailedAsync(1, cts.Token));
        Assert.Empty(h.Api.ThreadIds);
    }

    [Fact]
    public async Task StalePositiveStateCannotAcknowledgeOrEnableTransmission()
    {
        await using var h = new Harness();
        await h.StartAsync(true);
        await h.AdvanceAsync(TimeSpan.FromSeconds(4));
        Assert.False(h.Connection.FailureState.IsAvailable);
        Assert.Null(h.Connection.FailureState.Engine1Failed);
        await h.AssertUnavailableAsync();
        Assert.Empty(h.Api.Transmissions);
    }

    [Fact]
    public async Task WorkerRechecksFreshnessAfterQueueAdmission()
    {
        await using var h = new Harness();
        await h.StartAsync(false);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Api.Enqueue(action: () =>
        {
            entered.SetResult(); release.Wait(TimeSpan.FromSeconds(5)); h.Clock.Advance(TimeSpan.FromSeconds(4));
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pending = h.Actuator.EnsureEngineFailedAsync(1);
        release.Set();
        Assert.Equal(SimulatorFailureActuationStatus.SimulatorUnavailable, (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        Assert.Empty(h.Api.Transmissions);
    }

    [Fact]
    public async Task ObservingStateNeverAutomaticallyTransmitsAndOptionalRejectionCompletesPendingWork()
    {
        await using var h = new Harness();
        await h.StartAsync(false);
        await h.ObserveAsync(true);
        await h.ObserveAsync(false);
        await h.AdvanceAsync(TimeSpan.FromSeconds(12));
        await h.ObserveAsync(false);
        Assert.Empty(h.Api.Transmissions);
        var pending = h.Actuator.EnsureEngineFailedAsync(1);
        await Until(() => h.Api.Transmissions.Count == 1);
        var request = h.Api.FailureSetupPackets.Single(p => p.Operation == "request");
        await h.PacketAsync(SimConnectPackets.Exception(3, request.SendId));
        Assert.Equal(SimulatorFailureActuationStatus.SimulatorUnavailable, (await pending).Status);
        await h.CoreTelemetryAsync();
        Assert.Single(h.Api.Transmissions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task InvalidOptionalReadbackFailsClosedWithoutBreakingCoreTelemetry(int invalid)
    {
        await using var h = new Harness();
        await h.StartAsync(false);
        byte[] packet = invalid switch
        {
            0 => StatePacket(double.NaN), 1 => StatePacket(double.PositiveInfinity), 2 => StatePacket(2),
            3 => StatePacket(), _ => StatePacket(0, 1)
        };
        await h.PacketAsync(packet);
        await h.AssertUnavailableAsync();
        Assert.Empty(h.Api.Transmissions);
        await h.CoreTelemetryAsync();
        await h.ObserveAsync(true);
        Assert.Equal(SimulatorFailureActuationStatus.AlreadyFailed, (await h.Actuator.EnsureEngineFailedAsync(1)).Status);
    }

    [Fact]
    public async Task WrongRequestOrDefinitionCannotBecomeAcknowledgement()
    {
        await using var h = new Harness();
        await h.StartAsync(false);
        var pending = h.Actuator.EnsureEngineFailedAsync(1);
        await Until(() => h.Api.Transmissions.Count == 1);
        await h.PacketAsync(SimConnectPackets.SimObjectData(42, SimConnectEngineFailureDefinition.DefinitionId, [1]));
        await h.PacketAsync(SimConnectPackets.SimObjectData(SimConnectEngineFailureDefinition.RequestId, 42, [1]));
        Assert.False(pending.IsCompleted);
        await h.AdvanceAsync(TimeSpan.FromSeconds(11));
        Assert.Equal(SimulatorFailureActuationStatus.AcknowledgementTimeout, (await pending).Status);
        Assert.Single(h.Api.Transmissions);
    }

    [Fact]
    public async Task MissingTransmitExportOnlyDisablesOptionalActuator()
    {
        await using var h = new Harness();
        h.Api.TransmitException = new EntryPointNotFoundException();
        await h.StartAsync(false);
        await h.AssertUnavailableAsync();
        await h.CoreTelemetryAsync();
        Assert.Empty(h.Api.Transmissions);
    }

    [Fact]
    public async Task LostPacketCorrelationNeverClaimsSuccessOrRetransmits()
    {
        await using var h = new Harness();
        await h.StartAsync(false);
        h.Api.GetLastSentPacketIdResult = SimConnectTestTransport.Failure;
        var pending = h.Actuator.EnsureEngineFailedAsync(1);
        await Until(() => h.Api.Transmissions.Count == 1);
        await h.AdvanceAsync(TimeSpan.FromSeconds(11));
        Assert.Equal(SimulatorFailureActuationStatus.AcknowledgementTimeout, (await pending).Status);
        await h.ObserveAsync(false);
        Assert.Equal(SimulatorFailureActuationStatus.AcknowledgementTimeout, (await h.Actuator.EnsureEngineFailedAsync(1)).Status);
        Assert.Single(h.Api.Transmissions);
    }

    [Fact]
    public async Task UnrelatedExceptionKeepsExistingConnectionErrorBehavior()
    {
        await using var h = new Harness();
        await h.StartAsync(false);
        var pending = h.Actuator.EnsureEngineFailedAsync(1);
        await Until(() => h.Api.Transmissions.Count == 1);
        h.Api.Enqueue(SimConnectPackets.Exception(5, 123));
        Assert.Equal(SimulatorFailureActuationStatus.SimulatorUnavailable, (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        await Until(() => h.Connection.Current.Issue == SimulatorConnectionIssue.VersionMismatch);
        Assert.Single(h.Api.Transmissions);
    }

    [Fact]
    public void NativeAbiAndApplicationAuthorityStaySeparated()
    {
        var map = typeof(NativeSimConnectApi).GetMethod("SimConnect_MapClientEventToSimEvent", BindingFlags.NonPublic | BindingFlags.Static)!;
        var send = typeof(NativeSimConnectApi).GetMethod("SimConnect_TransmitClientEvent", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(new[] { typeof(nint), typeof(uint), typeof(string) }, map.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(new[] { typeof(nint), typeof(uint), typeof(uint), typeof(uint), typeof(uint), typeof(uint) }, send.GetParameters().Select(p => p.ParameterType));
        Assert.All(new[] { map, send }, method =>
        {
            Assert.Equal(typeof(int), method.ReturnType);
            var abi = method.GetCustomAttribute<DllImportAttribute>()!;
            Assert.Equal("SimConnect.dll", abi.Value);
            Assert.Equal(CallingConvention.StdCall, abi.CallingConvention);
            Assert.True(abi.ExactSpelling);
        });
        Assert.Equal(CharSet.Ansi, map.GetCustomAttribute<DllImportAttribute>()!.CharSet);
        Assert.Equal("EnsureEngineFailedAsync", Assert.Single(typeof(ISimulatorFailureActuator).GetMethods()).Name);
        Assert.DoesNotContain(typeof(SimConnectConnection).GetMethods(), m => m.Name.Contains("Toggle") || m.Name.Contains("Transmit"));
        Assert.DoesNotContain(typeof(AirframeFailureEligibilityService).GetConstructors().SelectMany(c => c.GetParameters()),
            p => p.ParameterType == typeof(ISimulatorFailureActuator));
        var composition = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "UiContracts", "App.xaml.cs"));
        Assert.Contains("AddSingleton<ISimulatorFailureActuator>(provider =>\n            provider.GetRequiredService<SimConnectConnection>())", composition.Replace("\r", ""));
        Assert.Contains("AddSingleton<ISimulatorFailureStateSource>(provider =>\n            provider.GetRequiredService<SimConnectConnection>())", composition.Replace("\r", ""));
    }
}
