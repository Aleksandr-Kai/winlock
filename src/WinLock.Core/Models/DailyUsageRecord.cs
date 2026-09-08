namespace WinLock.Core.Models;

/// <summary>How much of a given calendar day's budget was actually spent — kept purely for a
/// parent to look back at, never read by the enforcement logic itself (that only ever cares
/// about <see cref="UsageState.RemainingBudget"/> for <see cref="UsageState.BudgetDate"/>).
/// Only ever updated for "today"; once a day rolls over its record is final.</summary>
public sealed record DailyUsageRecord(DateOnly Date, TimeSpan UsedTime);
