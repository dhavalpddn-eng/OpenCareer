using OpenCareer.Application.Tutorials;

namespace OpenCareer.Tests;

public sealed class TutorialFocusTargetTests
{
    [Fact]
    public void BuiltInDashboardAndCurrentFlightStepsDeclareRealFocusTargets()
    {
        var catalog = new AppTutorialCatalog();

        TutorialDefinition intro =
            Assert.IsType<TutorialDefinition>(
                catalog.Get(AppTutorialCatalog.AppIntroId));

        TutorialStep dashboard = Assert.Single(
            intro.Steps,
            static step => step.Id == "dashboard");

        TutorialStep currentFlight = Assert.Single(
            intro.Steps,
            static step => step.Id == "current-flight");

        Assert.Equal(
            AppTutorialCatalog.DashboardFlightDeskFocusKey,
            dashboard.FocusElementKey);

        Assert.Equal(
            AppTutorialCatalog.CurrentFlightSessionFocusKey,
            currentFlight.FocusElementKey);
    }

    [Fact]
    public void ContextualConnectionGuidanceUsesImplementedPageFocusTargets()
    {
        var catalog = new AppTutorialCatalog();

        TutorialStep connected = Assert.Single(
            Assert.IsType<TutorialDefinition>(
                catalog.Get(
                    AppTutorialCatalog.FirstSimulatorConnectionId))
                .Steps);

        TutorialStep disconnected = Assert.Single(
            Assert.IsType<TutorialDefinition>(
                catalog.Get(
                    AppTutorialCatalog.FirstSimulatorDisconnectId))
                .Steps);

        Assert.Equal(
            AppTutorialCatalog.DashboardFlightDeskFocusKey,
            connected.FocusElementKey);

        Assert.Equal(
            AppTutorialCatalog.CurrentFlightSessionFocusKey,
            disconnected.FocusElementKey);
    }

    [Fact]
    public void UnimplementedDestinationsDoNotClaimControlFocusTargets()
    {
        TutorialDefinition intro =
            Assert.IsType<TutorialDefinition>(
                new AppTutorialCatalog().Get(
                    AppTutorialCatalog.AppIntroId));

        TutorialStep jobs = Assert.Single(
            intro.Steps,
            static step => step.Id == "jobs");

        TutorialStep dispatch = Assert.Single(
            intro.Steps,
            static step => step.Id == "dispatch");

        Assert.Null(jobs.FocusElementKey);
        Assert.Null(dispatch.FocusElementKey);
    }
}
