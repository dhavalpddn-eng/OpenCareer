namespace OpenCareer.Application.Tutorials;

public sealed class TutorialCoordinator(
    ITutorialCatalog catalog,
    ITutorialFeatureReadiness featureReadiness,
    ITutorialProgressStore progressStore,
    ITutorialStepEvidenceSource? liveEvidence = null)
{
    public event EventHandler? StateChanged;
    public event EventHandler<TutorialNavigationRequestedEventArgs>? NavigationRequested;

    public TutorialSnapshot Current { get; private set; } = TutorialSnapshot.Inactive;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        TutorialDefinition definition = RequireDefinition(AppTutorialCatalog.AppIntroId);
        TutorialProgress progress = await progressStore
            .GetAsync(definition.Id, cancellationToken)
            .ConfigureAwait(true);

        if (progress.CompletedVersion >= definition.Version ||
            progress.SkippedVersion >= definition.Version)
        {
            SetInactive(definition);
            return;
        }

        int index = FindResumeIndex(definition, progress.LastStepId);
        await ActivateAsync(definition, progress, index, cancellationToken).ConfigureAwait(true);
    }

    public async Task StartAsync(
        string tutorialId,
        bool resume = true,
        CancellationToken cancellationToken = default)
    {
        TutorialDefinition definition = RequireDefinition(tutorialId);
        TutorialProgress progress = await progressStore
            .GetAsync(tutorialId, cancellationToken)
            .ConfigureAwait(true);

        int index = resume ? FindResumeIndex(definition, progress.LastStepId) : 0;
        await ActivateAsync(definition, progress, index, cancellationToken).ConfigureAwait(true);
    }

    public Task RestartAsync(string tutorialId, CancellationToken cancellationToken = default) =>
        StartAsync(tutorialId, resume: false, cancellationToken);

    public async Task<bool> TryStartPendingAsync(
        string tutorialId,
        CancellationToken cancellationToken = default)
    {
        if (Current.IsActive)
            return false;

        TutorialDefinition definition = RequireDefinition(tutorialId);
        TutorialProgress progress = await progressStore
            .GetAsync(tutorialId, cancellationToken)
            .ConfigureAwait(true);

        if (progress.CompletedVersion >= definition.Version
            || progress.SkippedVersion >= definition.Version)
        {
            return false;
        }

        int index = FindResumeIndex(
            definition,
            progress.LastStepId);

        await ActivateAsync(
                definition,
                progress,
                index,
                cancellationToken)
            .ConfigureAwait(true);

        return true;
    }

    public async Task NextAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetActive(out TutorialDefinition definition, out TutorialProgress progress))
            return;

        if (Current.IsLastStep)
        {
            TutorialProgress completed = progress with
            {
                CompletedVersion = Math.Max(progress.CompletedVersion, definition.Version),
                LastStepId = null
            };

            await progressStore.SaveAsync(completed, cancellationToken).ConfigureAwait(true);
            SetInactive(definition);
            return;
        }

        int nextIndex = Current.CurrentIndex + 1;
        await MoveAsync(definition, progress, nextIndex, cancellationToken).ConfigureAwait(true);
    }

    public async Task BackAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetActive(out TutorialDefinition definition, out TutorialProgress progress) ||
            Current.CurrentIndex <= 0)
        {
            return;
        }

        await MoveAsync(
            definition,
            progress,
            Current.CurrentIndex - 1,
            cancellationToken).ConfigureAwait(true);
    }

    public void RefreshLiveEvidence()
    {
        if (!Current.IsActive
            || _activeDefinition is null)
        {
            return;
        }

        TutorialSnapshot refreshed =
            CreateSnapshot(
                _activeDefinition,
                Current.CurrentIndex);

        if (refreshed.EvidenceState
            == Current.EvidenceState)
        {
            return;
        }

        Current = refreshed;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SkipAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetActive(out TutorialDefinition definition, out TutorialProgress progress))
            return;

        TutorialProgress skipped = progress with
        {
            SkippedVersion = Math.Max(progress.SkippedVersion, definition.Version),
            LastStepId = null
        };

        await progressStore.SaveAsync(skipped, cancellationToken).ConfigureAwait(true);
        SetInactive(definition);
    }

    private TutorialDefinition RequireDefinition(string tutorialId)
    {
        TutorialDefinition? definition = catalog.Get(tutorialId);
        if (definition is null || definition.Steps.Count == 0)
            throw new InvalidOperationException($"Tutorial '{tutorialId}' is not defined.");

        return definition;
    }

    private async Task ActivateAsync(
        TutorialDefinition definition,
        TutorialProgress progress,
        int index,
        CancellationToken cancellationToken)
    {
        TutorialStep step = definition.Steps[index];
        TutorialProgress activeProgress = progress with { LastStepId = step.Id };
        await progressStore.SaveAsync(activeProgress, cancellationToken).ConfigureAwait(true);

        Current = CreateSnapshot(definition, index);
        _activeDefinition = definition;
        _activeProgress = activeProgress;
        PublishState();
    }

    private async Task MoveAsync(
        TutorialDefinition definition,
        TutorialProgress progress,
        int index,
        CancellationToken cancellationToken)
    {
        TutorialStep step = definition.Steps[index];
        TutorialProgress moved = progress with { LastStepId = step.Id };
        await progressStore.SaveAsync(moved, cancellationToken).ConfigureAwait(true);

        Current = CreateSnapshot(definition, index);
        _activeProgress = moved;
        PublishState();
    }

    private TutorialSnapshot CreateSnapshot(TutorialDefinition definition, int index)
    {
        TutorialStep step = definition.Steps[index];
        return new TutorialSnapshot(
            definition.Id,
            definition.Version,
            true,
            index,
            definition.Steps.Count,
            step,
            featureReadiness.GetState(step.FeatureKey),
            liveEvidence?.GetState(step)
                ?? TutorialStepEvidenceState.NotApplicable);
    }

    private void SetInactive(TutorialDefinition definition)
    {
        _activeDefinition = null;
        _activeProgress = null;
        Current = new TutorialSnapshot(
            definition.Id,
            definition.Version,
            false,
            -1,
            definition.Steps.Count,
            null,
            TutorialFeatureState.Unavailable);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PublishState()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
        string? navigationTag = Current.Step?.NavigationTag;
        if (!string.IsNullOrWhiteSpace(navigationTag))
        {
            NavigationRequested?.Invoke(
                this,
                new TutorialNavigationRequestedEventArgs(navigationTag));
        }
    }

    private bool TryGetActive(
        out TutorialDefinition definition,
        out TutorialProgress progress)
    {
        if (Current.IsActive &&
            _activeDefinition is TutorialDefinition activeDefinition &&
            _activeProgress is TutorialProgress activeProgress)
        {
            definition = activeDefinition;
            progress = activeProgress;
            return true;
        }

        definition = null!;
        progress = null!;
        return false;
    }

    private static int FindResumeIndex(TutorialDefinition definition, string? stepId)
    {
        if (string.IsNullOrWhiteSpace(stepId))
            return 0;

        for (int i = 0; i < definition.Steps.Count; i++)
        {
            if (string.Equals(definition.Steps[i].Id, stepId, StringComparison.Ordinal))
                return i;
        }

        return 0;
    }

    private TutorialDefinition? _activeDefinition;
    private TutorialProgress? _activeProgress;
}
