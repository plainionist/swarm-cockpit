<#
.SYNOPSIS
    Point a Windows port-proxy at the current WSL2 IP so LAN machines can reach
    the Swarm Cockpit service running inside WSL2.

.DESCRIPTION
    WSL2 uses a NAT'd virtual network, so binding 0.0.0.0 inside WSL is not
    reachable from other machines on the LAN, and the WSL IP changes on every
    WSL restart. This script forwards <host>:Port -> <currentWslIp>:Port and is
    safe to run repeatedly (idempotent). It does nothing if no WSL distro is
    currently running, so it will not wake WSL from a background timer.

    Run once manually, or let install-cockpit-portproxy.ps1 schedule it.
#>
[CmdletBinding()]
param(
    [int]$Port = 5959,
    [string]$ListenAddress = "0.0.0.0"
)

$ErrorActionPreference = "SilentlyContinue"

function Invoke-WslRaw {
    # wsl.exe subcommands (--list/--status) emit UTF-16LE; decode and strip nulls.
    param([string[]]$WslArgs)
    $prev = [Console]::OutputEncoding
    try {
        [Console]::OutputEncoding = [System.Text.Encoding]::Unicode
        $raw = & wsl.exe @WslArgs 2>$null
    } finally {
        [Console]::OutputEncoding = $prev
    }
    return ($raw | ForEach-Object { ($_ -replace "`0", "").Trim() } | Where-Object { $_ -ne "" })
}

# Only act when a distro is already running - avoids waking WSL from the timer.
$running = Invoke-WslRaw @("--list", "--running", "--quiet")
if (-not $running) {
    Write-Output "No running WSL distro; nothing to forward."
    exit 0
}

# `hostname -I` runs inside the distro and returns normal UTF-8.
$hostOut = (wsl.exe hostname -I 2>$null)
$wslIp = @($hostOut -split '\s+' | Where-Object { $_ -match '^\d{1,3}(\.\d{1,3}){3}$' })[0]
if (-not $wslIp) {
    Write-Output "Could not determine WSL IP; leaving port-proxy unchanged."
    exit 0
}

# Read the current forward target for this listener, if any.
$current = $null
$show = netsh interface portproxy show v4tov4 2>$null
foreach ($line in $show) {
    if ($line -match "^\s*$([regex]::Escape($ListenAddress))\s+$Port\s+(\d{1,3}(?:\.\d{1,3}){3})\s+$Port\s*$") {
        $current = $Matches[1]
    }
}

if ($current -ne $wslIp) {
    netsh interface portproxy delete v4tov4 listenport=$Port listenaddress=$ListenAddress 2>$null | Out-Null
    netsh interface portproxy add v4tov4 listenport=$Port listenaddress=$ListenAddress connectport=$Port connectaddress=$wslIp | Out-Null
    Write-Output "port-proxy updated: ${ListenAddress}:${Port} -> ${wslIp}:${Port}"
}
else {
    Write-Output "port-proxy already correct: ${ListenAddress}:${Port} -> ${wslIp}:${Port}"
}

# Ensure the inbound firewall rule exists.
if (-not (Get-NetFirewallRule -DisplayName "Swarm Cockpit ${Port}" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "Swarm Cockpit ${Port}" -Direction Inbound -Action Allow `
        -Protocol TCP -LocalPort $Port -Profile Any | Out-Null
    Write-Output "created firewall rule 'Swarm Cockpit ${Port}'"
}
