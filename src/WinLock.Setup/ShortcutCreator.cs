namespace WinLock.Setup;

/// <summary>Creates the Start Menu "WinLock — Настройка" shortcut, using the same late-bound
/// WScript.Shell COM object PowerShell's New-Object -ComObject WScript.Shell wraps — no
/// interop assembly needed for a single CreateShortcut call.</summary>
public static class ShortcutCreator
{
    public static void CreatePairingShortcut(string installDir)
    {
        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        var shortcutPath = Path.Combine(startMenu, "WinLock — Настройка.lnk");

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell COM-объект недоступен.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = Path.Combine(installDir, "WinLock.Agent.UI.exe");
            shortcut.Arguments = "--pair";
            shortcut.WorkingDirectory = installDir;
            shortcut.Description = "Привязать телефон родителя к WinLock (требуются права администратора)";
            shortcut.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }

        // .lnk "Run as administrator" is a single bit with no COM property for it — patch the
        // file directly. Byte 21 (0-indexed) holds the link flags; bit 0x20 is
        // RunAsAdministrator. A non-admin child double-clicking this shortcut must hit a UAC
        // prompt they can't answer, not silently run unelevated.
        var bytes = File.ReadAllBytes(shortcutPath);
        bytes[21] |= 0x20;
        File.WriteAllBytes(shortcutPath, bytes);
    }

    public static void RemovePairingShortcut()
    {
        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        var shortcutPath = Path.Combine(startMenu, "WinLock — Настройка.lnk");
        if (File.Exists(shortcutPath))
            File.Delete(shortcutPath);
    }
}
