#Requires -Version 5.1
<#
.SYNOPSIS
    Installs the WinLock agent (Windows service + lock-screen/setup UI) on this machine.
.DESCRIPTION
    Must be run elevated (Run as Administrator). Installs to Program Files, registers the
    "WinLock Agent" service (auto-start, auto-restart on crash), restricts the data folder
    to SYSTEM/Administrators, opens the firewall for the network channel, and adds a Start
    Menu shortcut for pairing.
#>

$ErrorActionPreference = 'Stop'

$ServiceName    = 'WinLock Agent'
$InstallDir     = Join-Path $env:ProgramFiles 'WinLock'
$DataDir        = Join-Path $env:ProgramData 'WinLock'
$NetworkPort    = 51843
$PayloadDir     = Join-Path $PSScriptRoot 'payload'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Error "Запустите этот скрипт от имени администратора (правой кнопкой -> 'Запуск с правами администратора')."
        exit 1
    }
}

function Assert-Payload {
    $serviceExe = Join-Path $PayloadDir 'Service\WinLock.Service.exe'
    $uiExe      = Join-Path $PayloadDir 'UI\WinLock.Agent.UI.exe'
    if (-not (Test-Path $serviceExe) -or -not (Test-Path $uiExe)) {
        Write-Error "Не найден payload в '$PayloadDir'. Соберите его сначала: build-payload.sh (или опубликуйте вручную Service и UI в payload\Service и payload\UI)."
        exit 1
    }
}

function Test-DotNetRuntimes {
    try {
        $runtimes = & dotnet --list-runtimes 2>$null
    } catch {
        $runtimes = $null
    }

    $hasAspNetCore = $runtimes -and ($runtimes -match 'Microsoft\.AspNetCore\.App 8\.')
    $hasDesktop    = $runtimes -and ($runtimes -match 'Microsoft\.WindowsDesktop\.App 8\.')

    if (-not $hasAspNetCore) {
        Write-Warning "Не найден ASP.NET Core Runtime 8.x (x64) — служба WinLock Agent не запустится без него."
    }
    if (-not $hasDesktop) {
        Write-Warning "Не найден .NET Desktop Runtime 8.x (x64) — экран блокировки и Setup-инструмент не запустятся без него."
    }
    if (-not $hasAspNetCore -or -not $hasDesktop) {
        Write-Warning "Скачайте оба (в разделе 'Run desktop apps' и 'Run server apps'): https://dotnet.microsoft.com/download/dotnet/8.0"
        $answer = Read-Host "Продолжить установку без них? Служба, скорее всего, не запустится. (y/N)"
        if ($answer -notmatch '^[yY]') {
            Write-Host "Установка прервана. Установите нужный(е) рантайм(ы) и запустите скрипт снова."
            exit 1
        }
    }
}

function Install-Files {
    Write-Host "Устанавливаем файлы в $InstallDir ..."
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    Copy-Item -Path (Join-Path $PayloadDir 'Service\*') -Destination $InstallDir -Recurse -Force
    Copy-Item -Path (Join-Path $PayloadDir 'UI\*') -Destination $InstallDir -Recurse -Force
}

function Protect-DataDir {
    Write-Host "Настраиваем каталог данных $DataDir ..."
    New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
    # Only SYSTEM and Administrators — a standard child account gets no access at all here.
    # (The one subfolder the child's own screenshot helper needs to write into sets its own,
    # narrower exception at runtime — see ScreenCaptureCoordinator.) Group names like
    # "Administrators" are localized (e.g. "Администраторы" on Russian Windows) and icacls
    # can fail to resolve the English name there — well-known SIDs (*S-1-5-18 = SYSTEM,
    # *S-1-5-32-544 = Administrators) work regardless of display language.
    & icacls $DataDir /inheritance:r | Out-Null
    & icacls $DataDir /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
}

# Deliberately never deletes-and-recreates an existing service on install: sc.exe delete
# only *marks* a service for deletion, and the SCM won't actually drop it while anything
# still holds a handle (an open Services snap-in, a stale PowerShell ServiceController
# object, sometimes even after the process that made it is gone) — that race is exactly
# what caused CreateService to fail with "marked for deletion" here. Updating an existing
# service's config in place sidesteps the whole class of problem. Deletion still happens,
# deliberately, in Uninstall-WinLock.ps1, where there's no immediate recreate racing it.
function Install-Service {
    $exePath = Join-Path $InstallDir 'WinLock.Service.exe'
    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

    if ($existing) {
        Write-Host "Служба '$ServiceName' уже существует — обновляем её на месте..."
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        & sc.exe config $ServiceName binPath= "`"$exePath`"" start= auto | Out-Null
        & sc.exe description $ServiceName 'Контроль и ограничение времени использования компьютера. Не останавливайте эту службу.' | Out-Null
    } else {
        Write-Host "Регистрируем службу '$ServiceName'..."
        New-Service -Name $ServiceName `
            -BinaryPathName "`"$exePath`"" `
            -DisplayName $ServiceName `
            -Description 'Контроль и ограничение времени использования компьютера. Не останавливайте эту службу.' `
            -StartupType Automatic | Out-Null
    }

    # Restart on crash: up to 3 restarts/day, 5s apart, then leave it stopped rather than
    # loop forever if something is fundamentally broken.
    & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null
    & sc.exe failureflag $ServiceName 1 | Out-Null
}

function Grant-FirewallRule {
    $ruleName = 'WinLock Agent'
    if (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue) {
        Remove-NetFirewallRule -DisplayName $ruleName
    }
    Write-Host "Открываем порт $NetworkPort для входящих подключений родительского приложения..."
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP `
        -LocalPort $NetworkPort -Action Allow `
        -Program (Join-Path $InstallDir 'WinLock.Service.exe') | Out-Null
}

function New-PairingShortcut {
    Write-Host "Добавляем ярлык 'WinLock — Настройка' в меню Пуск..."
    $startMenu = [Environment]::GetFolderPath('CommonStartMenu')
    $shortcutPath = Join-Path $startMenu 'WinLock — Настройка.lnk'

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = Join-Path $InstallDir 'WinLock.Agent.UI.exe'
    $shortcut.Arguments = '--pair'
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.Description = 'Привязать телефон родителя к WinLock (требуются права администратора)'
    $shortcut.Save()

    # .lnk "Run as administrator" is a single bit with no cmdlet for it — patch the file
    # directly. Byte 21 (0-indexed) holds the link flags; bit 0x20 is RunAsAdministrator.
    # This matters here specifically: a non-admin child double-clicking this shortcut must
    # hit a UAC prompt they can't answer, not silently run unelevated.
    $bytes = [IO.File]::ReadAllBytes($shortcutPath)
    $bytes[21] = $bytes[21] -bor 0x20
    [IO.File]::WriteAllBytes($shortcutPath, $bytes)
}

function Start-WinLockService {
    Write-Host "Запускаем службу..."
    $exePath = Join-Path $InstallDir 'WinLock.Service.exe'
    try {
        Start-Service -Name $ServiceName -ErrorAction Stop
        Start-Sleep -Seconds 2
        $svc = Get-Service -Name $ServiceName
        if ($svc.Status -ne 'Running') {
            throw "статус после запуска: $($svc.Status)"
        }
        Write-Host "Служба запущена." -ForegroundColor Green
    } catch {
        Write-Warning "Служба не запустилась: $($_.Exception.Message)"
        Write-Warning "Самый частый повод — не установлен нужный .NET Runtime. Чтобы увидеть точную ошибку, выполните:"
        Write-Warning "  & `"$exePath`""
        Write-Warning "(она покажет исключение прямо в консоли; Ctrl+C, чтобы остановить). Также можно проверить"
        Write-Warning "Просмотр событий Windows -> Журналы Windows -> Приложение."
    }
}

Assert-Administrator
Assert-Payload
Test-DotNetRuntimes
Install-Files
Protect-DataDir
Install-Service
Grant-FirewallRule
New-PairingShortcut
Start-WinLockService

Write-Host ""
Write-Host "=== Установка завершена ===" -ForegroundColor Green
Write-Host "1. Убедитесь, что учётная запись ребёнка — ОБЫЧНАЯ (не администратор)."
Write-Host "2. Запустите ярлык 'WinLock — Настройка' из меню Пуск (потребуется подтверждение UAC)"
Write-Host "   и отсканируйте QR-код в родительском приложении, чтобы привязать телефон."
Write-Host "3. По умолчанию расписание пустое — компьютер будет заблокирован, пока вы не"
Write-Host "   зададите расписание/лимит из родительского приложения после привязки."
