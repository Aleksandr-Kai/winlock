using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO.Pipes;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using WinLock.Core.Ipc;
using WinLock.Core.Windows;

// Standalone, self-contained tool: run it directly on the child's PC (from the same USB drive
// as the installer) when the phone can't reach the service, and it can't be diagnosed remotely.
// Every check is independent and best-effort -- one failing (or requiring admin rights this
// run doesn't have) must never stop the rest from running and being written to the report.

[SupportedOSPlatform("windows")]
static class Diagnostics
{
    private const int NetworkPort = 51843;

    static void Main()
    {
        var report = new StringBuilder();

        void Line(string text)
        {
            Console.WriteLine(text);
            report.AppendLine(text);
        }

        void Section(string title)
        {
            Console.WriteLine();
            Line($"=== {title} ===");
        }

        void RunCheck(string title, Action<Action<string>> check)
        {
            Section(title);
            try
            {
                check(Line);
            }
            catch (Exception ex)
            {
                Line($"[Ошибка при проверке] {ex.GetType().Name}: {ex.Message}");
            }
        }

        Line("WinLock — диагностика ПК");
        Line($"Время отчёта: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        Line($"Компьютер: {Environment.MachineName}, пользователь: {Environment.UserName}, права администратора: {IsAdministrator()}");
        Line($"ОС: {Environment.OSVersion}, x64: {Environment.Is64BitOperatingSystem}");

        RunCheck(".NET Runtimes", w => w(ProcessRunner.Run("dotnet.exe", "--list-runtimes").StandardOutput.Trim()));

        RunCheck("Служба WinLock Agent", CheckService);
        RunCheck("Установленные файлы", CheckInstalledFiles);
        RunCheck("Процессы", CheckProcesses);
        RunCheck("Именованный канал (pipe) — то, через что общаются экран блокировки/окно настройки со службой", CheckPipe);
        RunCheck($"Сеть — TCP порт {NetworkPort}", CheckNetworkPort);
        RunCheck("Правила брандмауэра", CheckFirewallRules);
        RunCheck("Данные (%ProgramData%\\WinLock)", CheckDataDirectory);
        RunCheck("Регистрация запуска в безопасном режиме", CheckSafeModeRegistration);
        RunCheck("Журнал событий Windows (Приложение, последние 3 дня, записи похожие на сбой)", CheckEventLog);
        RunCheck("Журнал событий Windows (Система, последние 3 дня, входы/выходы и перезагрузки)", CheckSessionEvents);

        var outPath = Path.Combine(AppContext.BaseDirectory, $"WinLock-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(outPath, report.ToString(), Encoding.UTF8);

        Console.WriteLine();
        Console.WriteLine($"Готово. Результат сохранён в: {outPath}");
        Console.WriteLine("Скопируйте этот файл или пришлите его — нажмите Enter для выхода...");
        Console.ReadLine();
    }

    private static void CheckService(Action<string> w)
    {
        var name = ServiceControl.ServiceName;
        w($"Установлена: {ServiceControl.Exists(name)}");
        w($"Запущена (по мнению ServiceControl): {ServiceControl.IsRunning(name)}");
        w("");
        w("--- sc query ---");
        w(ProcessRunner.Run("sc.exe", "query", name).StandardOutput.Trim());
        w("--- sc qc (путь к бинарнику, тип запуска) ---");
        w(ProcessRunner.Run("sc.exe", "qc", name).StandardOutput.Trim());
    }

    private static void CheckInstalledFiles(Action<string> w)
    {
        var installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinLock");
        w($"Каталог: {installDir}, существует: {Directory.Exists(installDir)}");
        if (!Directory.Exists(installDir))
            return;

        foreach (var fileName in new[] { "WinLock.Service.exe", "WinLock.Agent.UI.exe" })
        {
            var path = Path.Combine(installDir, fileName);
            if (!File.Exists(path))
            {
                w($"{fileName}: ОТСУТСТВУЕТ");
                continue;
            }

            var info = new FileInfo(path);
            var version = FileVersionInfo.GetVersionInfo(path);
            w($"{fileName}: {info.Length} байт, изменён {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}, версия файла {version.FileVersion}");
        }
    }

    private static void CheckProcesses(Action<string> w)
    {
        foreach (var name in new[] { "WinLock.Service", "WinLock.Agent.UI" })
        {
            var processes = Process.GetProcessesByName(name);
            if (processes.Length == 0)
            {
                w($"{name}: не запущен");
                continue;
            }

            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        w($"{name}: PID {process.Id}, старт {process.StartTime:yyyy-MM-dd HH:mm:ss}, память {process.WorkingSet64 / 1024 / 1024} МБ");
                    }
                    catch (Exception ex)
                    {
                        w($"{name}: PID {process.Id} (не удалось прочитать подробности: {ex.Message})");
                    }
                }
            }
        }
    }

    private static void CheckPipe(Action<string> w)
    {
        using var pipe = new NamedPipeClientStream(".", IpcEndpoints.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            pipe.Connect(2000);
            w("Подключение к каналу успешно установлено.");
        }
        catch (TimeoutException)
        {
            w("ТАЙМАУТ: канал не отвечает за 2 секунды — служба не слушает канал, зависла, или недоступна. Это и есть, скорее всего, причина 'не могу подключиться'.");
        }
    }

    private static void CheckNetworkPort(Action<string> w)
    {
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        var onOurPort = listeners.Where(l => l.Port == NetworkPort).ToList();
        w($"Порт {NetworkPort} слушается (по данным ОС): {onOurPort.Count > 0}");
        foreach (var l in onOurPort)
            w($"  слушает на {l.Address}:{l.Port}");

        w("");
        w("Локальные IPv4-адреса этого ПК (для сравнения с тем, что видит телефон):");
        foreach (var address in GetLocalIPv4Addresses())
            w($"  {address}" + (IPAddress.IsLoopback(address) ? "  <-- ЭТО LOOPBACK, телефон не сможет по нему подключиться!" : ""));

        w("");
        TryTcpConnect(w, IPAddress.Loopback, NetworkPort);
        foreach (var address in GetLocalIPv4Addresses().Where(a => !IPAddress.IsLoopback(a)))
            TryTcpConnect(w, address, NetworkPort);
    }

    private static void TryTcpConnect(Action<string> w, IPAddress address, int port)
    {
        using var client = new TcpClient();
        try
        {
            var connectTask = client.ConnectAsync(address, port);
            if (connectTask.Wait(2000) && client.Connected)
                w($"{address}:{port} — TCP-соединение установлено успешно.");
            else
                w($"{address}:{port} — не удалось подключиться за 2с (порт закрыт, служба не слушает на этом адресе, или блокирует файрвол).");
        }
        catch (Exception ex)
        {
            w($"{address}:{port} — ошибка: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static IEnumerable<IPAddress> GetLocalIPv4Addresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork);

    private static void CheckFirewallRules(Action<string> w)
    {
        w("--- WinLock Agent ---");
        w(ProcessRunner.Run("netsh.exe", "advfirewall", "firewall", "show", "rule", "name=WinLock Agent").StandardOutput.Trim());
        w("--- WinLock Agent (mDNS discovery) ---");
        w(ProcessRunner.Run("netsh.exe", "advfirewall", "firewall", "show", "rule", "name=WinLock Agent (mDNS discovery)").StandardOutput.Trim());
    }

    private static void CheckDataDirectory(Action<string> w)
    {
        w($"Каталог: {AgentDataPathsCompat.DataDir}, существует: {Directory.Exists(AgentDataPathsCompat.DataDir)}");
        if (!Directory.Exists(AgentDataPathsCompat.DataDir))
            return;

        foreach (var file in Directory.GetFiles(AgentDataPathsCompat.DataDir))
        {
            var info = new FileInfo(file);
            var flag = info.Name.Contains(".corrupt-") || info.Name.EndsWith(".errors.log")
                ? "  <-- признак того, что state.json когда-то не удалось прочитать!"
                : "";
            w($"  {info.Name}: {info.Length} байт, изменён {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}{flag}");
        }
    }

    private static void CheckSafeModeRegistration(Action<string> w)
    {
        foreach (var safeBootType in new[] { "Minimal", "Network" })
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Control\SafeBoot\{safeBootType}\{ServiceControl.ServiceName}");
            w($"{safeBootType}: {(key != null ? "зарегистрирована" : "НЕ зарегистрирована")}");
        }
    }

    private static void CheckEventLog(Action<string> w)
    {
        // Last 3 days, expressed in milliseconds for the FilterXPath "timediff" function.
        var query = new EventLogQuery(
            "Application", PathType.LogName,
            "*[System[TimeCreated[timediff(@SystemTime) <= 259200000]]]");
        using var reader = new EventLogReader(query);

        var found = 0;
        for (var entry = reader.ReadEvent(); entry != null && found < 100; entry = reader.ReadEvent())
        {
            using (entry)
            {
                var provider = entry.ProviderName ?? "";
                string? description = null;
                try { description = entry.FormatDescription(); }
                catch { /* some providers' metadata isn't resolvable locally — skip the text, keep the rest */ }

                var relevant =
                    provider.Contains(".NET Runtime", StringComparison.OrdinalIgnoreCase) ||
                    provider.Contains("Application Error", StringComparison.OrdinalIgnoreCase) ||
                    provider.Contains("Service Control Manager", StringComparison.OrdinalIgnoreCase) ||
                    (description?.Contains("WinLock", StringComparison.OrdinalIgnoreCase) ?? false);

                if (!relevant)
                    continue;

                found++;
                w($"[{entry.TimeCreated:yyyy-MM-dd HH:mm:ss}] {provider} (уровень={entry.LevelDisplayName}, id={entry.Id})");
                w(description ?? "(нет описания)");
                w("---");
            }
        }

        w($"Найдено релевантных записей: {found}");
    }

    private static void CheckSessionEvents(Action<string> w)
    {
        // System log, not Application: Winlogon (logon/logoff), User Profile Service (profile
        // loaded/unloaded — logged unconditionally, unlike Security-log logon events, which
        // need an audit policy most home PCs never turn on), who/why initiated a
        // shutdown/restart (User32 event 1074), and Kernel-Power's "the system rebooted
        // without a clean shutdown first" (41) plus sleep/wake (42/107/1). Together these
        // show what was actually going on around a burst of lock-screen relaunches — logons,
        // logoffs, sleep cycles, hard power loss — instead of having to guess from the
        // watchdog's own log lines alone.
        var query = new EventLogQuery(
            "System", PathType.LogName,
            "*[System[TimeCreated[timediff(@SystemTime) <= 259200000]]]");
        using var reader = new EventLogReader(query);

        var found = 0;
        for (var entry = reader.ReadEvent(); entry != null && found < 200; entry = reader.ReadEvent())
        {
            using (entry)
            {
                var provider = entry.ProviderName ?? "";
                var relevant =
                    provider.Contains("Winlogon", StringComparison.OrdinalIgnoreCase) ||
                    provider.Contains("User Profile Service", StringComparison.OrdinalIgnoreCase) ||
                    (provider.Equals("User32", StringComparison.OrdinalIgnoreCase) && entry.Id == 1074) ||
                    (provider.Contains("Kernel-Power", StringComparison.OrdinalIgnoreCase) && entry.Id is 41 or 42 or 107 or 1);

                if (!relevant)
                    continue;

                string? description = null;
                try { description = entry.FormatDescription(); }
                catch { /* some providers' metadata isn't resolvable locally — skip the text, keep the rest */ }

                found++;
                w($"[{entry.TimeCreated:yyyy-MM-dd HH:mm:ss}] {provider} (id={entry.Id})");
                w(description ?? "(нет описания)");
                w("---");
            }
        }

        w($"Найдено релевантных записей: {found}");
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}

/// <summary>Mirrors WinLock.Core.Windows.AgentDataPaths (added on a branch not merged into
/// this diagnostic tool's baseline) so this tool builds standalone against the commit it was
/// branched from.</summary>
file static class AgentDataPathsCompat
{
    public static string DataDir => Environment.GetEnvironmentVariable("WINLOCK_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinLock");
}
