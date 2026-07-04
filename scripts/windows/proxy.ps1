# Start-SwarmCockpitProxy.ps1
# Run as Administrator

param(
    [Parameter(Position = 0)]
    [ValidateSet("start", "stop")]
    [string]$Verb = "start",

    [Alias("Host")]
    [Parameter(Position = 1)]
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
    throw "CockpitHost is required when Verb is 'start'. Use -CockpitHost or -Host."
}

$ipHelper = Get-Service -Name iphlpsvc -ErrorAction SilentlyContinue
if (-not $ipHelper) {
    throw "Required service 'iphlpsvc' (IP Helper) was not found."
}

if ($ipHelper.Status -ne "Running") {
    try {
        if ($ipHelper.StartType -eq "Disabled") {
            Set-Service -Name iphlpsvc -StartupType Manual -ErrorAction Stop
        }

        Start-Service -Name iphlpsvc -ErrorAction Stop
    }
    catch {
        throw "IP Helper service (iphlpsvc) must be running for portproxy. Re-run this script as Administrator, then try again."
    }
}

$defaultRoute = Get-NetRoute -AddressFamily IPv4 -DestinationPrefix "0.0.0.0/0" |
    Sort-Object RouteMetric, InterfaceMetric |
    Select-Object -First 1

$listenIp = $null

if ($defaultRoute) {
    $listenIp = (
        Get-NetIPAddress -AddressFamily IPv4 -InterfaceIndex $defaultRoute.InterfaceIndex |
        Where-Object {
            $_.IPAddress -notlike "127.*" -and
            $_.PrefixOrigin -ne "WellKnown" -and
            $_.InterfaceAlias -notlike "*Loopback*"
        } |
        Select-Object -First 1 -ExpandProperty IPAddress
    )
}

if (-not $listenIp) {
    $listenIp = (
        Get-NetIPAddress -AddressFamily IPv4 |
        Where-Object {
            $_.IPAddress -notlike "127.*" -and
            $_.PrefixOrigin -ne "WellKnown" -and
            $_.InterfaceAlias -notlike "*Loopback*"
        } |
        Select-Object -First 1 -ExpandProperty IPAddress
    )
}

if (-not $listenIp) {
    throw "Could not determine a non-loopback IPv4 address to listen on."
}

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