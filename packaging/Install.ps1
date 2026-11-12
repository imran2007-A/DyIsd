# Installs (or updates) DyIsd. Double-click Install.bat instead of running this directly.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

# Trusting the certificate needs admin, so ask for it once.
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    exit
}

try {
    Write-Host "Closing DyIsd if it's running..."
    Get-Process DyIsd -ErrorAction SilentlyContinue | Stop-Process -Force

    Write-Host "Trusting the DyIsd certificate..."
    Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=DyIsd Dev' | Remove-Item
    Import-Certificate -FilePath (Join-Path $here 'DyIsd.cer') -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null

    Write-Host "Installing DyIsd..."
    Add-AppxPackage -Path (Join-Path $here 'DyIsd.msix') -ForceApplicationShutdown

    $pkg = Get-AppxPackage -Name DyIsd
    Write-Host "Installed version $($pkg.Version). Starting it now."
    Start-Process "shell:AppsFolder\$($pkg.PackageFamilyName)!App"
}
catch {
    Write-Host ""
    Write-Host "Install failed: $($_.Exception.Message)" -ForegroundColor Red
}
Write-Host ""
Read-Host "Press Enter to close"
