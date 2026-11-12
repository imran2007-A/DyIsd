# Builds DyIsd.msix from a published app folder and signs it with a fresh self-signed certificate.
# Used by GitHub Actions. Runs on Windows only (needs the Windows SDK tools).
param(
    [Parameter(Mandatory)] [string] $AppDir,
    [Parameter(Mandatory)] [string] $OutDir,
    [Parameter(Mandatory)] [string] $Version
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-SdkTool($name) {
    $tool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\$name" -ErrorAction SilentlyContinue |
        Sort-Object { [version]($_.Directory.Parent.Name) } -Descending | Select-Object -First 1
    if (-not $tool) { throw "$name not found. Is the Windows 10/11 SDK installed?" }
    return $tool.FullName
}
$makeappx = Find-SdkTool 'makeappx.exe'
$signtool = Find-SdkTool 'signtool.exe'

# 1. Stage: app files + manifest + logos
$stage = Join-Path $env:RUNNER_TEMP 'msix-stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage, $OutDir | Out-Null
Copy-Item "$AppDir\*" $stage -Recurse
Copy-Item "$root\Assets" "$stage\Assets" -Recurse -Force
(Get-Content "$root\AppxManifest.xml" -Raw).Replace('__VERSION__', $Version) |
    Set-Content "$stage\AppxManifest.xml" -Encoding UTF8

# 2. Pack
$msix = Join-Path $OutDir 'DyIsd.msix'
& $makeappx pack /d $stage /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

# 3. Sign with a throwaway certificate. The .cer (public part only) ships with the installer.
$cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=DyIsd Dev' -KeyUsage DigitalSignature `
    -FriendlyName 'DyIsd Dev' -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
$pwd = ConvertTo-SecureString -String ([guid]::NewGuid().ToString()) -Force -AsPlainText
$pfx = Join-Path $env:RUNNER_TEMP 'dyisd.pfx'
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pwd | Out-Null
Export-Certificate -Cert $cert -FilePath (Join-Path $OutDir 'DyIsd.cer') | Out-Null
$plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($pwd))
& $signtool sign /fd SHA256 /f $pfx /p $plain $msix
if ($LASTEXITCODE -ne 0) { throw "signtool failed" }
Remove-Item $pfx -Force

# 4. Installer scripts
Copy-Item "$root\Install.ps1", "$root\Install.bat" $OutDir
Write-Host "Built $msix (version $Version)"
