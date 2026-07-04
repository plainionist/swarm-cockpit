<#
.SYNOPSIS
    One-time setup so the Swarm Cockpit stays reachable from the LAN across WSL
    restarts and machine reboots.

.DESCRIPTION
    Registers a scheduled task that runs update-cockpit-portproxy.ps1:
      * at every user logon (survives machine reboots), and
      * every few minutes while logged on (catches WSL IP changes when WSL
        restarts without a full reboot).
    The task runs with your account and highest privileges, so netsh/firewall
    changes apply silently (no UAC prompt) and it can see your WSL distro.

    Run this ONCE from an elevated (Administrator) PowerShell:
        powershell -ExecutionPolicy Bypass -File scripts\windows\install-cockpit-portproxy.ps1
#>
[CmdletBinding()]
param(
    [int]$Port = 5959,
    [int]$RefreshMinutes = 2,
    [string]$TaskName = "SwarmCockpitPortProxy"
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Run this script from an elevated (Administrator) PowerShell."
    exit 1
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$updateScript = Join-Path $scriptDir "update-cockpit-portproxy.ps1"
if (-not (Test-Path $updateScript)) {
    Write-Error "Cannot find update script at $updateScript"
    exit 1
}

$action = New-ScheduledTaskAction -Execute "powershell.exe" `
    -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$updateScript`" -Port $Port"

# Fire at logon, then repeat every RefreshMinutes for the life of the session.
$trigger = New-ScheduledTaskTrigger -AtLogOn
$repeat = New-ScheduledTaskTrigger -Once -At (Get-Date) `
    -RepetitionInterval (New-TimeSpan -Minutes $RefreshMinutes) `
    -RepetitionDuration (New-TimeSpan -Days 3650)
$trigger.Repetition = $repeat.Repetition

$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" `
    -LogonType Interactive -RunLevel Highest

$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -StartWhenAvailable -MultipleInstances IgnoreNew

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
    -Principal $principal -Settings $settings -Force | Out-Null

Write-Output "Registered scheduled task '$TaskName' (at logon + every $RefreshMinutes min)."

# Apply immediately so it works right now.
& $updateScript -Port $Port

Write-Output ""
Write-Output "Done. From another machine, browse to: http://<this-host-LAN-IP>:$Port"
Write-Output "To remove: Unregister-ScheduledTask -TaskName '$TaskName' -Confirm:`$false"
