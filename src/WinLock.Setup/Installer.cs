using System.Security.AccessControl;
using System.Security.Principal;

namespace WinLock.Setup;

public sealed class InstallException(string message) : Exception(message);

/// <summary>
/// Copies the payload into Program Files, locks down %ProgramData%\WinLock, registers (or,
/// if it's already there, updates in place) the "WinLock Agent" service, opens the firewall,
/// adds the pairing shortcut, and starts the service. Runs entirely from this already-
/// elevated GUI process — no PowerShell, no console, no separate "Run as administrator" step.
/// </summary>
public static class Installer
{
    public const string ServiceName = "WinLock Agent";
    private const string ServiceDescription = "Контроль и ограничение времени использования компьютера. Не останавливайте эту службу.";
    private const string FirewallRuleName = "WinLock Agent";
    private const string MdnsFirewallRuleName = "WinLock Agent (mDNS discovery)";
    private const int NetworkPort = 51843;
    private const int MdnsPort = 5353;

    public static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinLock");

    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinLock");

    public static bool IsAlreadyInstalled() => ServiceControl.Exists(ServiceName);

    public static void Run(string payloadDir, IProgress<string> log)
    {
        var serviceExe = Path.Combine(payloadDir, "Service", "WinLock.Service.exe");
        var uiExe = Path.Combine(payloadDir, "UI", "WinLock.Agent.UI.exe");
        if (!File.Exists(serviceExe) || !File.Exists(uiExe))
            throw new InstallException($"Не найден payload в '{payloadDir}'. Установщик должен лежать рядом с папкой payload.");

        CheckDotNetRuntimes(log);

        var alreadyInstalled = ServiceControl.Exists(ServiceName);
        if (alreadyInstalled && ServiceControl.IsRunning(ServiceName))
        {
            log.Report("Останавливаем службу перед обновлением файлов...");
            ServiceControl.Stop(ServiceName);
        }

        log.Report($"Копируем файлы в {InstallDir}...");
        CopyPayload(payloadDir, InstallDir);

        log.Report($"Настраиваем каталог данных {DataDir}...");
        ProtectDataDir(DataDir);

        var installedExePath = Path.Combine(InstallDir, "WinLock.Service.exe");
        if (alreadyInstalled)
        {
            log.Report($"Служба '{ServiceName}' уже существует — обновляем её на месте...");
            ServiceControl.UpdateInPlace(ServiceName, installedExePath, ServiceDescription);
        }
        else
        {
            log.Report($"Регистрируем службу '{ServiceName}'...");
            ServiceControl.Create(ServiceName, ServiceName, installedExePath, ServiceDescription);
        }

        log.Report($"Открываем порт {NetworkPort} в брандмауэре...");
        OpenFirewallPort(Path.Combine(InstallDir, "WinLock.Service.exe"));

        log.Report("Добавляем ярлык «WinLock — Настройка» в меню Пуск...");
        ShortcutCreator.CreatePairingShortcut(InstallDir);

        log.Report("Запускаем службу...");
        if (ServiceControl.Start(ServiceName))
        {
            log.Report("Служба запущена.");
        }
        else
        {
            log.Report("ПРЕДУПРЕЖДЕНИЕ: служба не запустилась в течение 15 секунд.");
            log.Report("Самая частая причина — не установлен ASP.NET Core Runtime 8.x (x64).");
            log.Report("Подробности можно посмотреть в Просмотре событий Windows -> Журналы Windows -> Приложение.");
        }
    }

    private static void CheckDotNetRuntimes(IProgress<string> log)
    {
        string stdout;
        try
        {
            stdout = ProcessRunner.Run("dotnet.exe", "--list-runtimes").StandardOutput;
        }
        catch (Exception)
        {
            stdout = string.Empty;
        }

        var hasAspNetCore = stdout.Contains("Microsoft.AspNetCore.App 8.");
        var hasDesktop = stdout.Contains("Microsoft.WindowsDesktop.App 8.");

        if (!hasAspNetCore)
            log.Report("ПРЕДУПРЕЖДЕНИЕ: не найден ASP.NET Core Runtime 8.x (x64) — служба WinLock Agent не запустится без него.");
        if (!hasDesktop)
            log.Report("ПРЕДУПРЕЖДЕНИЕ: не найден .NET Desktop Runtime 8.x (x64) — экран блокировки не запустится без него.");
        if (!hasAspNetCore || !hasDesktop)
            log.Report("Скачать оба (разделы 'Run desktop apps' и 'Run server apps'): https://dotnet.microsoft.com/download/dotnet/8.0 — установка продолжится, но служба, скорее всего, не заработает без них.");
    }

    private static void CopyPayload(string payloadDir, string installDir)
    {
        Directory.CreateDirectory(installDir);
        CopyDirectoryContents(Path.Combine(payloadDir, "Service"), installDir);
        CopyDirectoryContents(Path.Combine(payloadDir, "UI"), installDir);
    }

    private static void CopyDirectoryContents(string sourceDir, string destDir)
    {
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, sourceFile);
            var destFile = Path.Combine(destDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(sourceFile, destFile, overwrite: true);
        }
    }

    /// <summary>Only SYSTEM and Administrators get access — a standard child account gets no
    /// access at all here. (The one subfolder the child's own screenshot helper needs to
    /// write into sets its own, narrower exception at runtime.) Well-known SIDs are used
    /// instead of localized group names so this works regardless of Windows' display
    /// language.</summary>
    private static void ProtectDataDir(string dataDir)
    {
        var info = Directory.CreateDirectory(dataDir);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    private static void OpenFirewallPort(string programPath)
    {
        ProcessRunner.Run("netsh.exe", "advfirewall", "firewall", "delete", "rule", $"name={FirewallRuleName}");
        ProcessRunner.Run("netsh.exe", "advfirewall", "firewall", "add", "rule",
            $"name={FirewallRuleName}", "dir=in", "action=allow", "protocol=TCP",
            $"localport={NetworkPort}", $"program={programPath}", "enable=yes");

        // Without this, a phone's mDNS query for _winlock._tcp.local never reaches the PC at
        // all — Windows Firewall blocks unsolicited inbound UDP by default, and that block
        // applies to our own multicast socket the same as anything else. DiscoveryBeacon can
        // send its own advertisements out fine either way (outbound is normally allowed), but
        // without this rule nobody's *query* gets through, so "find automatically" silently
        // finds nothing even though the PC is advertising.
        ProcessRunner.Run("netsh.exe", "advfirewall", "firewall", "delete", "rule", $"name={MdnsFirewallRuleName}");
        ProcessRunner.Run("netsh.exe", "advfirewall", "firewall", "add", "rule",
            $"name={MdnsFirewallRuleName}", "dir=in", "action=allow", "protocol=UDP",
            $"localport={MdnsPort}", $"program={programPath}", "enable=yes");
    }
}
