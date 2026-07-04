# Start-SwarmCockpitProxy.ps1
# Run as Administrator

param(
    [ValidateSet("start", "stop")]
    [string]$Verb = "start",

    [string]$CockpitHost,

    [int]$CockpitPort = 5959,
    [int]$ListenPort = 9595
)

if ($Verb -eq "stop") {
    netsh interface portproxy reset
    Remove-NetFirewallRule -DisplayName "SwarmCockpit phone proxy 9595" -ErrorAction SilentlyContinue
    return
}

if ([string]::IsNullOrWhiteSpace($CockpitHost)) {
    throw "CockpitHost is required when Verb is 'start'."
}

$listenIp = (
    Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object {
        $_.IPAddress -notlike "127.*" -and
        $_.PrefixOrigin -ne "WellKnown" -and
        $_.InterfaceAlias -notlike "*Loopback*"
    } |
    Select-Object -First 1 -ExpandProperty IPAddress
)

Write-Host "Laptop listen IP: $listenIp"
Write-Host "Forwarding http://$listenIp`:$ListenPort -> $CockpitHost`:$CockpitPort"

# Remove old rule if present
netsh interface portproxy delete v4tov4 `
    listenaddress=$listenIp `
    listenport=$ListenPort `
    protocol=tcp | Out-Null

# Add proxy
netsh interface portproxy add v4tov4 `
    listenaddress=$listenIp `
    listenport=$ListenPort `
    connectaddress=$CockpitHost `
    connectport=$CockpitPort `
    protocol=tcp

# Firewall
$ruleName = "SwarmCockpit phone proxy $ListenPort"
Remove-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue

New-NetFirewallRule `
    -DisplayName $ruleName `
    -Direction Inbound `
    -Action Allow `
    -Protocol TCP `
    -LocalPort $ListenPort | Out-Null

Write-Host ""
Write-Host "Open this on your phone:"
Write-Host "http://$listenIp`:$ListenPort"
Write-Host ""
Write-Host "Current portproxy rules:"
netsh interface portproxy show all