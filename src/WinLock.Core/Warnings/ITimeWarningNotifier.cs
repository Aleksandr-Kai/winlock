namespace WinLock.Core.Warnings;

/// <summary>Shows the child a brief "time is running out" notice. Implemented on Windows by
/// launching a small overlay into the interactive session; a no-op everywhere else.</summary>
public interface ITimeWarningNotifier
{
    void Notify(int minutesRemaining);
}
