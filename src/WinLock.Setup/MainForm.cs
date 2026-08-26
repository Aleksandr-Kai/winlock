using System.Drawing;
using System.Windows.Forms;

namespace WinLock.Setup;

public sealed class MainForm : Form
{
    private readonly Label _statusLabel;
    private readonly TextBox _log;
    private readonly Button _primaryButton;
    private readonly Button _uninstallButton;
    private readonly Button _closeButton;

    private readonly string _payloadDir = Path.Combine(AppContext.BaseDirectory, "payload");
    private bool _running;

    public MainForm()
    {
        Text = "WinLock — Установка";
        ClientSize = new Size(560, 460);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        var title = new Label
        {
            Text = "WinLock",
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 20),
        };

        _statusLabel = new Label
        {
            AutoSize = false,
            Location = new Point(24, 58),
            Size = new Size(512, 40),
            Text = "Проверяем текущее состояние...",
        };

        _log = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(24, 104),
            Size = new Size(512, 280),
            BackColor = Color.White,
            Font = new Font("Consolas", 9f),
        };

        _primaryButton = new Button
        {
            Text = "Установить",
            Location = new Point(24, 396),
            Size = new Size(160, 34),
        };
        _primaryButton.Click += async (_, _) => await RunInstallAsync();

        _uninstallButton = new Button
        {
            Text = "Удалить WinLock",
            Location = new Point(196, 396),
            Size = new Size(160, 34),
        };
        _uninstallButton.Click += async (_, _) => await RunUninstallAsync();

        _closeButton = new Button
        {
            Text = "Закрыть",
            Location = new Point(456, 396),
            Size = new Size(80, 34),
        };
        _closeButton.Click += (_, _) => Close();

        Controls.AddRange([title, _statusLabel, _log, _primaryButton, _uninstallButton, _closeButton]);

        Load += (_, _) => RefreshState();
    }

    private void RefreshState()
    {
        var installed = Installer.IsAlreadyInstalled();
        _statusLabel.Text = installed
            ? $"WinLock уже установлен на этом компьютере (служба \"{Installer.ServiceName}\" найдена).\nНажмите «Обновить», чтобы установить текущую версию поверх."
            : "WinLock ещё не установлен. Нажмите «Установить», чтобы начать.";
        _primaryButton.Text = installed ? "Обновить" : "Установить";
        _uninstallButton.Visible = installed;
    }

    private async Task RunInstallAsync()
    {
        if (!File.Exists(Path.Combine(_payloadDir, "Service", "WinLock.Service.exe")))
        {
            MessageBox.Show(this,
                $"Не найдена папка payload рядом с установщиком:\n{_payloadDir}\n\nУстановщик должен запускаться из папки installer целиком, не отдельно.",
                "WinLock", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        await RunWithLogAsync(progress => Installer.Run(_payloadDir, progress), "Установка завершена.");
    }

    private async Task RunUninstallAsync()
    {
        var confirm = MessageBox.Show(this,
            "Удалить WinLock с этого компьютера?",
            "WinLock", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes)
            return;

        var removeData = MessageBox.Show(this,
            "Также удалить сохранённые данные (расписание, привязанные телефоны родителей)?\n\n" +
            "Если оставить их — при повторной установке телефоны привязывать заново не потребуется.",
            "WinLock", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2)
            == DialogResult.Yes;

        await RunWithLogAsync(progress => Uninstaller.Run(removeData, progress), "Удаление завершено.");
    }

    private async Task RunWithLogAsync(Action<IProgress<string>> action, string successMessage)
    {
        if (_running) return;
        _running = true;
        _log.Clear();
        SetButtonsEnabled(false);

        IProgress<string> progress = new Progress<string>(line => _log.AppendText(line + Environment.NewLine));

        try
        {
            await Task.Run(() => action(progress));
            MessageBox.Show(this, successMessage, "WinLock", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            progress.Report($"ОШИБКА: {ex.Message}");
            MessageBox.Show(this, $"Не удалось завершить операцию:\n{ex.Message}\n\nПодробности — в журнале выше.",
                "WinLock", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetButtonsEnabled(true);
            RefreshState();
            _running = false;
        }
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _primaryButton.Enabled = enabled;
        _uninstallButton.Enabled = enabled;
        _closeButton.Enabled = enabled;
    }
}
