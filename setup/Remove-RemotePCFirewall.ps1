$ErrorActionPreference = "Stop"
$ruleName = "RemotePC Host"

$existing = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
if ($existing) {
    $existing | Remove-NetFirewallRule
    Write-Host "RemotePC firewall rule removed."
} else {
    Write-Host "RemotePC firewall rule was not present."
}
