using WinLock.Core;
using WinLock.Core.Locking;
using WinLock.Core.Models;
using WinLock.Core.Network;
using WinLock.Core.State;
using WinLock.Core.Warnings;

namespace WinLock.Service;

/// <summary>
/// Polls the enforcement core on a fixed interval, drives the lock controller on state
/// transitions, and persists state so a reboot or crash mid-session loses no more than
/// one poll interval's worth of accounting.
/// </summary>
public sealed class EnforcementWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly AgentRuntime _runtime;
    private readonly IStateStore _stateStore;
    private readonly ILockController _lockController;
    private readonly IAgentStatusPublisher _statusPublisher;
    private readonly ITimeWarningNotifier _timeWarningNotifier;
    private readonly TimeWarningTracker _timeWarningTracker = new();
    private readonly ILogger<EnforcementWorker> _logger;

    public EnforcementWorker(
        AgentRuntime runtime,
        IStateStore stateStore,
        ILockController lockController,
        IAgentStatusPublisher statusPublisher,
        ITimeWarningNotifier timeWarningNotifier,
        ILogger<EnforcementWorker> logger)
    {
        _runtime = runtime;
        _stateStore = stateStore;
        _lockController = lockController;
        _statusPublisher = statusPublisher;
        _timeWarningNotifier = timeWarningNotifier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var wasLocked = false;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // The enforcement core must never bring the service down: a crash here
                    // would leave the machine unsupervised. Log and retry next tick instead.
                    var decision = _runtime.Evaluate();

                    if (decision.ShouldBeLocked != wasLocked)
                    {
                        if (decision.ShouldBeLocked)
                            await _lockController.LockAsync(decision.Reason, stoppingToken);
                        else
                            await _lockController.UnlockAsync(stoppingToken);

                        wasLocked = decision.ShouldBeLocked;
                    }

                    await _stateStore.SaveAsync(_runtime.Snapshot(), stoppingToken);
                    await _statusPublisher.PublishAsync(decision, stoppingToken);

                    var warningMinutes = _timeWarningTracker.Check(
                        decision.RemainingBudget, decision.ShouldBeLocked, _runtime.CurrentSchedule.IsConfigured);
                    if (warningMinutes is { } minutes)
                        _timeWarningNotifier.Notify(minutes);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Enforcement cycle failed; will retry next tick.");
                }

                await Task.Delay(PollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // normal on shutdown
        }
    }
}
