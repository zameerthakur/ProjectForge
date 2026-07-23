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
        }

        public ProvisioningRequirement Requirement { get; } = new()
        {
            Id = "local-model",
            DisplayName = "Local model",
            Version = "1",
            Kind = ProvisioningArtifactKind.Model
        };

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
