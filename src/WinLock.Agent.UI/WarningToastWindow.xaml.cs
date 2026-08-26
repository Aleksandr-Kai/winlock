using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace WinLock.Agent.UI;

/// <summary>
/// A brief, corner "time is running out" toast — shown for both the 15-minute warning and
/// the follow-ups every 5 minutes after. Deliberately does not take focus or accept input
/// (games and other fullscreen apps keep working underneath it, uninterrupted) but keeps
/// re-asserting itself topmost for as long as it's visible, since some fullscreen apps
/// periodically reclaim the top of the z-order themselves. True DirectX exclusive-fullscreen
/// games bypass the desktop compositor entirely and can still cover it — nothing short of
/// kicking the game out of fullscreen fixes that, which would be more disruptive than the
/// warning itself, so this is a best-effort guarantee, not an absolute one.
/// </summary>
public partial class WarningToastWindow : Window
{
    private static readonly TimeSpan VisibleFor = TimeSpan.FromSeconds(9);
    private static readonly TimeSpan TopmostReassertInterval = TimeSpan.FromSeconds(1.5);

    private readonly int _minutesRemaining;
    private DispatcherTimer? _topmostTimer;
    private DispatcherTimer? _closeTimer;

    public WarningToastWindow(int minutesRemaining)
    {
        _minutesRemaining = minutesRemaining;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        MessageText.Text = $"Осталось {_minutesRemaining} {PluralizeMinutes(_minutesRemaining)} компьютерного времени";

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 24;
        Top = workArea.Bottom - Height - 24;

        MakeNonActivatingAndClickThrough();
        ReassertTopmost();

        _topmostTimer = new DispatcherTimer { Interval = TopmostReassertInterval };
        _topmostTimer.Tick += (_, _) => ReassertTopmost();
        _topmostTimer.Start();

        _closeTimer = new DispatcherTimer { Interval = VisibleFor };
        _closeTimer.Tick += (_, _) => Close();
        _closeTimer.Start();
    }

    private void MakeNonActivatingAndClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, exStyle);
    }

    private void ReassertTopmost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero) return;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private static string PluralizeMinutes(int n)
    {
        var lastTwo = n % 100;
        var last = n % 10;
        if (lastTwo is >= 11 and <= 14) return "минут";
        return last switch
        {
            1 => "минута",
            >= 2 and <= 4 => "минуты",
            _ => "минут",
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _topmostTimer?.Stop();
        _closeTimer?.Stop();
        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    private const int GWL_EXSTYLE = -20;
    private const nint WS_EX_TOOLWINDOW = 0x00000080;
    private const nint WS_EX_TRANSPARENT = 0x00000020;
    private const nint WS_EX_LAYERED = 0x00080000;
    private const nint WS_EX_NOACTIVATE = 0x08000000;
    private static readonly nint HWND_TOPMOST = -1;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
