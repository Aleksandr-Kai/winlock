using WinLock.Core;
using WinLock.Core.Locking;
using WinLock.Core.Models;
using WinLock.Core.Network;
using WinLock.Core.State;
using WinLock.Core.Warnings;
using WinLock.Service.Security;

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
    private readonly ITouchpadGestureHardener _touchpadGestureHardener;
    private readonly IOrphanedLockProcessGuard _orphanedLockProcessGuard;
    private readonly TimeWarningTracker _timeWarningTracker = new();
    private readonly ILogger<EnforcementWorker> _logger;

    public EnforcementWorker(
        AgentRuntime runtime,
        IStateStore stateStore,
        ILockController lockController,
        IAgentStatusPublisher statusPublisher,
        ITimeWarningNotifier timeWarningNotifier,
        ITouchpadGestureHardener touchpadGestureHardener,
        IOrphanedLockProcessGuard orphanedLockProcessGuard,
        ILogger<EnforcementWorker> logger)
    {
        _runtime = runtime;
        _stateStore = stateStore;
        _lockController = lockController;
        _statusPublisher = statusPublisher;
        _timeWarningNotifier = timeWarningNotifier;
        _touchpadGestureHardener = touchpadGestureHardener;
        _orphanedLockProcessGuard = orphanedLockProcessGuard;
        _logger = logger;

        // Logged (rather than just tracked in UsageHistorySnapshot) so it shows up in the
        // Windows Event Log a diagnostic report already captures -- a clear "here's when and
        // how much was granted" trail is exactly what's missing when the phone-side numbers
        // alone don't add up and there's no way to see state.json's actual contents directly.
        _runtime.DailyBudgetGranted += (date, budget) =>
        {
            _logger.LogInformation("New daily budget granted: {Minutes} minutes for {Date:yyyy-MM-dd}.", (int)budget.TotalMinutes, date);

            // A fresh budget period should always start with nobody locked -- if a lock-screen
            // process is still around anyway, it's orphaned on some virtual desktop from the
            // previous lockout rather than having exited properly. See
            // OrphanedLockProcessGuard for why the response is a forced logoff.
            _orphanedLockProcessGuard.CheckForOrphanedLockProcess();
        };
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
                    _touchpadGestureHardener.Enforce();

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
