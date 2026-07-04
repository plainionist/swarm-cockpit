# Start-SwarmCockpitProxy.ps1
# Run as Administrator

param(
    [Parameter(Position = 0)]
    [ValidateSet("start", "stop")]
    [string]$Verb = "start",

    [Alias("Host")]
    [Parameter(Position = 1)]
    [string]$CockpitHost,

    [Alias("Ip")]
    [string]$ListenIp,

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

$connectedInterfaceIndexes = @(
    Get-NetConnectionProfile -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty InterfaceIndex -Unique
)

$upAdapterIndexes = @(
    Get-NetAdapter -ErrorAction SilentlyContinue |
    Where-Object { $_.Status -eq "Up" } |
    Select-Object -ExpandProperty ifIndex -Unique
)

$activeInterfaceIndexes = @(
    $connectedInterfaceIndexes |
    Where-Object { $upAdapterIndexes -contains $_ }
)

$eligibleIps = Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object {
        $_.IPAddress -notlike "127.*" -and
        $_.IPAddress -notlike "169.254.*" -and
        $_.PrefixOrigin -ne "WellKnown" -and
        $_.InterfaceAlias -notlike "*Loopback*" -and
        $_.InterfaceAlias -notlike "*vEthernet*" -and
        $_.InterfaceAlias -notlike "*Virtual*" -and
        $_.InterfaceAlias -notlike "*VMware*" -and
        $_.InterfaceAlias -notlike "*Hyper-V*" -and
        $_.InterfaceAlias -notlike "*WSL*" -and
        ($activeInterfaceIndexes.Count -eq 0 -or $activeInterfaceIndexes -contains $_.InterfaceIndex) -and
        $_.AddressState -eq "Preferred"
    }

$listenIps = @()

if ($ListenIp) {
    $matchingIp = $eligibleIps | Where-Object { $_.IPAddress -eq $ListenIp } | Select-Object -First 1
    if (-not $matchingIp) {
        throw "ListenIp '$ListenIp' is not a local non-loopback IPv4 address."
    }
    $listenIps = @($ListenIp)
}
else {
    # Default behavior: bind on all eligible local IPv4 addresses.
    $listenIps = @(
        $eligibleIps |
        Sort-Object @{ Expression = { if ($_.InterfaceAlias -match "^(Wi-?Fi|WLAN)") { 0 } else { 1 } } }, InterfaceMetric, SkipAsSource |
        Select-Object -ExpandProperty IPAddress -Unique
    )

    if (-not $listenIps -or $listenIps.Count -eq 0) {
        $defaultRoute = Get-NetRoute -AddressFamily IPv4 -DestinationPrefix "0.0.0.0/0" |
            Sort-Object RouteMetric, InterfaceMetric |
            Select-Object -First 1

        if ($defaultRoute) {
            $routeIp = $eligibleIps |
                Where-Object { $_.InterfaceIndex -eq $defaultRoute.InterfaceIndex } |
                Select-Object -First 1

            if ($routeIp) {
                $listenIps = @($routeIp.IPAddress)
            }
        }
    }
}

if (-not $listenIps -or $listenIps.Count -eq 0) {
    throw "Could not determine a non-loopback IPv4 address to listen on."
}

Write-Host "Laptop listen IPs: $($listenIps -join ', ')"
Write-Host "Forwarding $($listenIps.Count) address(es) on port $ListenPort -> $CockpitHost`:$CockpitPort"

foreach ($ip in $listenIps) {
    # Remove old rule if present
    netsh interface portproxy delete v4tov4 `
        listenaddress=$ip `
        listenport=$ListenPort `
        protocol=tcp | Out-Null

    # Add proxy
    netsh interface portproxy add v4tov4 `
        listenaddress=$ip `
        listenport=$ListenPort `
        connectaddress=$CockpitHost `
        connectport=$CockpitPort `
        protocol=tcp
}

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
Write-Host "Open one of these on your phone:"
foreach ($ip in $listenIps) {
    Write-Host "http://$ip`:$ListenPort"
}
Write-Host ""
Write-Host "Current portproxy rules:"
netsh interface portproxy show all