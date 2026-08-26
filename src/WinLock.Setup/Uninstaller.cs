namespace WinLock.Setup;

public static class Uninstaller
{
    public static void Run(bool removeData, IProgress<string> log)
    {
        if (ServiceControl.Exists(Installer.ServiceName))
        {
            log.Report($"Останавливаем и удаляем службу '{Installer.ServiceName}'...");
            ServiceControl.Stop(Installer.ServiceName);
            ServiceControl.Delete(Installer.ServiceName);
        }
        else
        {
            log.Report($"Служба '{Installer.ServiceName}' не найдена — пропускаем.");
        }

        log.Report("Удаляем правило брандмауэра...");
        ProcessRunner.Run("netsh.exe", "advfirewall", "firewall", "delete", "rule", "name=WinLock Agent");

        log.Report("Удаляем ярлык из меню Пуск...");
        ShortcutCreator.RemovePairingShortcut();

        if (Directory.Exists(Installer.InstallDir))
        {
            log.Report($"Удаляем файлы из {Installer.InstallDir}...");
            Directory.Delete(Installer.InstallDir, recursive: true);
        }

        if (removeData)
        {
            if (Directory.Exists(Installer.DataDir))
            {
                log.Report($"Удаляем данные {Installer.DataDir} (расписание, привязанные родители, состояние)...");
                Directory.Delete(Installer.DataDir, recursive: true);
            }
        }
        else
        {
            log.Report($"Данные в {Installer.DataDir} сохранены — повторная установка не потребует новой привязки телефонов.");
        }

        log.Report("Удаление завершено.");
    }
}
