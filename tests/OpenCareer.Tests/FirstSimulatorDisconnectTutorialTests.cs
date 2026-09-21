using OpenCareer.Application.Tutorials;

namespace OpenCareer.Tests;

public sealed class FirstSimulatorDisconnectTutorialTests
{
    [Fact]
    public void CatalogDefinesOneStepDisconnectRecoveryTutorial()
    {
        TutorialDefinition tutorial =
            Assert.IsType<TutorialDefinition>(
                new AppTutorialCatalog().Get(
                    AppTutorialCatalog.FirstSimulatorDisconnectId));

        Assert.Equal(1, tutorial.Version);

        TutorialStep step = Assert.Single(tutorial.Steps);
        Assert.Equal("simulator-disconnected", step.Id);
        Assert.Equal("current-flight", step.NavigationTag);
        Assert.Equal("current-flight", step.FeatureKey);
    }

    [Fact]
    public void StartingDisconnectedDoesNotCreateARecoveryEvent()
    {
        var trigger = new SimulatorDisconnectTutorialTrigger();

        trigger.Observe(isConnected: false);

        Assert.False(trigger.ShouldOffer);
    }

    [Fact]
    public void FirstConnectedToDisconnectedTransitionBecomesPending()
    {
        var trigger = new SimulatorDisconnectTutorialTrigger();

        trigger.Observe(isConnected: true);
        trigger.Observe(isConnected: false);

        Assert.True(trigger.ShouldOffer);
    }

    [Fact]
    public void PendingDisconnectSurvivesReconnectUntilHandled()
    {
        var trigger = new SimulatorDisconnectTutorialTrigger();

        trigger.Observe(isConnected: true);
        trigger.Observe(isConnected: false);
        trigger.Observe(isConnected: true);

        Assert.True(trigger.ShouldOffer);

        trigger.MarkHandled();

        Assert.False(trigger.ShouldOffer);

        trigger.Observe(isConnected: false);

        Assert.False(trigger.ShouldOffer);
    }
}
