param(
    [int]$Port = 47632
)

$ErrorActionPreference = "Stop"
$ruleName = "RemotePC Host"

if ($Port -lt 1 -or $Port -gt 65535) {
    throw "Port must be between 1 and 65535."
}

$existing = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
if ($existing) {
    $existing | Remove-NetFirewallRule
}

New-NetFirewallRule `
    -DisplayName $ruleName `
    -Direction Inbound `
    -Action Allow `
    -Protocol TCP `
    -LocalPort $Port `
    -Profile Private `
    -RemoteAddress 100.64.0.0/10 `
    -Description "Allows RemotePC host commands from Tailscale CGNAT addresses only."

Write-Host "RemotePC firewall rule created for TCP port $Port on Private networks, remote 100.64.0.0/10."
