using ProjectForge.Abstractions.Provisioning;
using ProjectForge.Core.Provisioning;

namespace ProjectForge.Core.Tests.Provisioning;

public sealed class RuntimeProvisioningCoordinatorTests
{
    [Fact]
    public async Task SkipsDependencyThatIsAlreadyReady()
    {
        var provisioner = new FakeProvisioner(isReady: true);
        await Coordinator(provisioner).EnsureReadyAsync();
        Assert.Equal(0, provisioner.ProvisionCount);
    }

    [Fact]
    public async Task NewCoordinatorReusesDependencyProvisionedBeforeRestart()
    {
        var provisioner = new FakeProvisioner();
        await Coordinator(provisioner).EnsureReadyAsync();

        await Coordinator(provisioner).EnsureReadyAsync();

        Assert.Equal(1, provisioner.ProvisionCount);
    }

    [Fact]
    public async Task ProvisionsAndVerifiesMissingDependency()
    {
        var provisioner = new FakeProvisioner();
        await Coordinator(provisioner).EnsureReadyAsync();
        Assert.Equal(1, provisioner.ProvisionCount);
        Assert.Equal(2, provisioner.ReadinessCheckCount);
    }

    [Fact]
    public async Task ReportsProvisioningLifecycle()
    {
        var progress = new RecordingProgress();

        await Coordinator(new FakeProvisioner()).EnsureReadyAsync(progress);

        Assert.Equal(
            [
                ProvisioningStatus.Checking,
                ProvisioningStatus.Downloading,
                ProvisioningStatus.Verifying,
                ProvisioningStatus.Ready
            ],
            progress.Updates.Select(update => update.Status));
    }

    [Fact]
    public async Task ConcurrentRequestsShareOneProvisioningOperation()
    {
        var provisioner = new FakeProvisioner(
            delay: TimeSpan.FromMilliseconds(50));
        var coordinator = Coordinator(provisioner);

        await Task.WhenAll(
            coordinator.EnsureReadyAsync(),
            coordinator.EnsureReadyAsync());

        Assert.Equal(1, provisioner.ProvisionCount);
    }

    [Fact]
    public async Task CanceledWaiterDoesNotStartDuplicateProvisioning()
    {
        var provisioningStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowProvisioning = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provisioner = new FakeProvisioner(
            provisioningStarted: provisioningStarted,
            allowProvisioning: allowProvisioning);
        var coordinator = Coordinator(provisioner);
        using var cancellation = new CancellationTokenSource();

        var canceledWaiter = coordinator.EnsureReadyAsync(
            cancellationToken: cancellation.Token);
        await provisioningStarted.Task;

        var activeWaiter = coordinator.EnsureReadyAsync();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => canceledWaiter);

        var laterWaiter = coordinator.EnsureReadyAsync();
        allowProvisioning.SetResult();
        await Task.WhenAll(activeWaiter, laterWaiter);

        Assert.Equal(1, provisioner.ProvisionCount);
    }

    [Fact]
    public async Task CancellationBeforeReadinessCheckDoesNotProvision()
    {
        var provisioner = new FakeProvisioner();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Coordinator(provisioner).EnsureReadyAsync(
                cancellationToken: cancellation.Token));

        Assert.Equal(0, provisioner.ProvisionCount);
        Assert.Equal(0, provisioner.ReadinessCheckCount);
    }

    [Fact]
    public async Task FailsWhenProvisionedDependencyCannotBeVerified()
    {
        var provisioner = new FakeProvisioner(becomesReady: false);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Coordinator(provisioner).EnsureReadyAsync());
    }

    [Fact]
    public async Task ReportsFailureWhenProvisionedDependencyCannotBeVerified()
    {
        var progress = new RecordingProgress();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Coordinator(new FakeProvisioner(becomesReady: false))
                .EnsureReadyAsync(progress));

        var failure = Assert.Single(
            progress.Updates,
            update => update.Status == ProvisioningStatus.Failed);
        Assert.Contains("verification failed", failure.Message);
    }

    [Fact]
    public async Task RetriesAfterFailedProvisioningOperation()
    {
        var provisioner = new FakeProvisioner(becomesReady: false);
        var coordinator = Coordinator(provisioner);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.EnsureReadyAsync());

        provisioner.BecomesReady = true;
        await coordinator.EnsureReadyAsync();

        Assert.Equal(2, provisioner.ProvisionCount);
    }

    [Fact]
    public async Task ReprovisionsDependencyThatBecomesUnready()
    {
        var provisioner = new FakeProvisioner();
        var coordinator = Coordinator(provisioner);

        await coordinator.EnsureReadyAsync();
        provisioner.IsReady = false;
        await coordinator.EnsureReadyAsync();

        Assert.Equal(2, provisioner.ProvisionCount);
    }

    [Fact]
    public void RejectsDuplicateProvisionerRegistrations()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Coordinator(new FakeProvisioner(), new FakeProvisioner()));

        Assert.Contains("local-model", exception.Message);
    }

    [Fact]
    public async Task EnsuresOnlyTheRequestedRequirement()
    {
        var first = new FakeProvisioner("first");
        var second = new FakeProvisioner("second");
        var coordinator = Coordinator(first, second);

        await coordinator.EnsureRequirementReadyAsync("second");

        Assert.Equal(0, first.ProvisionCount);
        Assert.Equal(1, second.ProvisionCount);
        Assert.False(coordinator.TryGetSnapshot("first", out _));
        Assert.True(coordinator.TryGetSnapshot("second", out var snapshot));
        Assert.Equal(ProvisioningStatus.Ready, snapshot.Status);
    }

    [Fact]
    public async Task RejectsUnknownRequirementIdentifier()
    {
        var coordinator = Coordinator(new FakeProvisioner());

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => coordinator.EnsureRequirementReadyAsync("unknown"));

        Assert.Contains("unknown", exception.Message);
    }

    [Fact]
    public async Task ConcurrentTargetedRequestsShareOneOperation()
    {
        var provisioner = new FakeProvisioner(
            id: "shared",
            delay: TimeSpan.FromMilliseconds(50));
        var coordinator = Coordinator(provisioner);

        await Task.WhenAll(
            coordinator.EnsureRequirementReadyAsync("shared"),
            coordinator.EnsureRequirementReadyAsync("shared"));

        Assert.Equal(1, provisioner.ProvisionCount);
        Assert.Equal(
            ProvisioningStatus.Ready,
            Assert.Single(coordinator.Snapshots).Status);
    }

    [Fact]
    public async Task TargetedFailureSnapshotIsReplacedAfterRetry()
    {
        var provisioner = new FakeProvisioner("retry", becomesReady: false);
        var coordinator = Coordinator(provisioner);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.EnsureRequirementReadyAsync("retry"));
        Assert.True(coordinator.TryGetSnapshot("retry", out var failed));
        Assert.Equal(ProvisioningStatus.Failed, failed.Status);

        provisioner.BecomesReady = true;
        await coordinator.EnsureRequirementReadyAsync("retry");

        Assert.True(coordinator.TryGetSnapshot("retry", out var ready));
        Assert.Equal(ProvisioningStatus.Ready, ready.Status);
        Assert.Equal(2, provisioner.ProvisionCount);
    }

    [Fact]
    public async Task CancelingTargetedWaiterPreservesSharedOperationAndState()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provisioner = new FakeProvisioner(
            id: "cancel",
            provisioningStarted: started,
            allowProvisioning: release);
        var coordinator = Coordinator(provisioner);
        using var cancellation = new CancellationTokenSource();

        var canceled = coordinator.EnsureRequirementReadyAsync(
            "cancel",
            cancellationToken: cancellation.Token);
        await started.Task;
        var active = coordinator.EnsureRequirementReadyAsync("cancel");
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.True(coordinator.TryGetSnapshot("cancel", out var downloading));
        Assert.Equal(ProvisioningStatus.Downloading, downloading.Status);

        release.SetResult();
        await active;

        Assert.True(coordinator.TryGetSnapshot("cancel", out var ready));
        Assert.Equal(ProvisioningStatus.Ready, ready.Status);
        Assert.Equal(1, provisioner.ProvisionCount);
    }

    [Fact]
    public async Task CoordinatorLifetimeCancellationStopsUnderlyingOperation()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var neverRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provisioner = new FakeProvisioner(
            id: "shutdown",
            provisioningStarted: started,
            allowProvisioning: neverRelease);
        using var shutdown = new CancellationTokenSource();
        var coordinator = new RuntimeProvisioningCoordinator(
            [provisioner],
            shutdown.Token);

        var operation = coordinator.EnsureRequirementReadyAsync("shutdown");
        await started.Task;
        shutdown.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.True(coordinator.TryGetSnapshot("shutdown", out var canceled));
        Assert.Equal(ProvisioningStatus.Canceled, canceled.Status);
        Assert.Equal(1, provisioner.ProvisionCount);
    }

    private static RuntimeProvisioningCoordinator Coordinator(
        params IRuntimeProvisioner[] provisioners) =>
        new(provisioners);

    private sealed class RecordingProgress : IProgress<ProvisioningProgress>
    {
        public List<ProvisioningProgress> Updates { get; } = [];

        public void Report(ProvisioningProgress value) => Updates.Add(value);
    }

    private sealed class FakeProvisioner : IRuntimeProvisioner
    {
        private readonly TimeSpan _delay;
        private readonly TaskCompletionSource? _provisioningStarted;
        private readonly TaskCompletionSource? _allowProvisioning;
        private bool _isReady;
        private int _provisionCount;
        private int _readinessCheckCount;

        public FakeProvisioner(
            string id = "local-model",
            bool isReady = false,
            bool becomesReady = true,
            TimeSpan delay = default,
            TaskCompletionSource? provisioningStarted = null,
            TaskCompletionSource? allowProvisioning = null)
        {
            _isReady = isReady;
            BecomesReady = becomesReady;
            _delay = delay;
            _provisioningStarted = provisioningStarted;
            _allowProvisioning = allowProvisioning;
            Requirement = new ProvisioningRequirement
            {
                Id = id,
                DisplayName = id,
                Version = "1",
                Kind = ProvisioningArtifactKind.Model
            };
        }

        public ProvisioningRequirement Requirement { get; }

        public int ProvisionCount => _provisionCount;

        public int ReadinessCheckCount => _readinessCheckCount;

        public bool BecomesReady { get; set; }

        public bool IsReady
        {
            get => _isReady;
            set => _isReady = value;
        }

        public Task<bool> IsReadyAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _readinessCheckCount);
            return Task.FromResult(_isReady);
        }

        public async Task ProvisionAsync(
            IProgress<ProvisioningProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _provisionCount);
            _provisioningStarted?.SetResult();

            if (_allowProvisioning is not null)
            {
                await _allowProvisioning.Task.WaitAsync(cancellationToken);
            }

            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }

            _isReady = BecomesReady;
        }
    }
}
