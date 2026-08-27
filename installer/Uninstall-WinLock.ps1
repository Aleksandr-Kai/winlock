#Requires -Version 5.1
<#
.SYNOPSIS
    Removes the WinLock agent from this machine.
.PARAMETER RemoveData
    Also deletes %ProgramData%\WinLock — the schedule, paired parents, and offline-unlock
    state. Without this switch that data is left in place, so reinstalling doesn't force
    re-pairing every parent's phone from scratch.
#>
param(
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'

$ServiceName = 'WinLock Agent'
$InstallDir  = Join-Path $env:ProgramFiles 'WinLock'
$DataDir     = Join-Path $env:ProgramData 'WinLock'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Error "Запустите этот скрипт от имени администратора."
        exit 1
    }
}

Assert-Administrator

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Останавливаем и удаляем службу '$ServiceName'..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $ServiceName | Out-Null
} else {
    Write-Host "Служба '$ServiceName' не найдена — пропускаем."
}

if (Get-NetFirewallRule -DisplayName 'WinLock Agent' -ErrorAction SilentlyContinue) {
    Write-Host "Удаляем правила брандмауэра..."
    Remove-NetFirewallRule -DisplayName 'WinLock Agent'
}
if (Get-NetFirewallRule -DisplayName 'WinLock Agent (mDNS discovery)' -ErrorAction SilentlyContinue) {
    Remove-NetFirewallRule -DisplayName 'WinLock Agent (mDNS discovery)'
}

$shortcutPath = Join-Path ([Environment]::GetFolderPath('CommonStartMenu')) 'WinLock — Настройка.lnk'
if (Test-Path $shortcutPath) {
    Write-Host "Удаляем ярлык из меню Пуск..."
    Remove-Item $shortcutPath -Force
}

if (Test-Path $InstallDir) {
    Write-Host "Удаляем файлы из $InstallDir ..."
    Remove-Item $InstallDir -Recurse -Force
}

if ($RemoveData) {
    if (Test-Path $DataDir) {
        Write-Host "Удаляем данные $DataDir (расписание, привязанные родители, состояние)..."
        Remove-Item $DataDir -Recurse -Force
    }
} else {
    Write-Host "Данные в $DataDir сохранены (повторная установка не потребует новой привязки)."
    Write-Host "Чтобы удалить и их: .\Uninstall-WinLock.ps1 -RemoveData"
}

Write-Host ""
Write-Host "=== Удаление завершено ===" -ForegroundColor Green
