# FigPin Local Sideloading Installer Script
# Self-elevates to administrator if needed, auto-extracts & trusts developer certificate from MSIX, and installs FigPin App

$msixPath = "$PSScriptRoot\edwinjoseph.FigPin_0.1.0.0_x64.msix"
$cerPath  = "$PSScriptRoot\FigPinStoreDev.cer"

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "             FigPin Studio - Local MSIX Package Installer" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $msixPath)) {
    # Search for any *.msix in current folder
    $foundMsix = Get-ChildItem -Path $PSScriptRoot -Filter "*.msix" | Select-Object -First 1
    if ($null -ne $foundMsix) {
        $msixPath = $foundMsix.FullName
    } else {
        Write-Host "[ERROR] Could not find MSIX package file in: $PSScriptRoot" -ForegroundColor Red
        Pause
        Exit
    }
}

# Auto-elevate to Administrator for Certificate Trust
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "[INFO] Requesting Administrator elevation to trust certificate locally..." -ForegroundColor Yellow
    Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    Exit
}

try {
    # Auto-generate / extract .cer certificate directly from the MSIX package if missing
    if (-not (Test-Path $cerPath)) {
        Write-Host "[INFO] Auto-extracting public signature certificate from MSIX package container..." -ForegroundColor Yellow
        $signerCert = (Get-AuthenticodeSignature -FilePath $msixPath).SignerCertificate
        if ($null -ne $signerCert) {
            $bytes = $signerCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
            [System.IO.File]::WriteAllBytes($cerPath, $bytes)
            Write-Host "[OK] Certificate 'FigPinStoreDev.cer' generated successfully!" -ForegroundColor Green
        } else {
            Write-Host "[WARNING] Could not extract certificate from MSIX signature." -ForegroundColor Yellow
        }
    }

    Write-Host "[1/2] Importing Developer Certificate into Local Machine Trusted Root..." -ForegroundColor Yellow
    if (Test-Path $cerPath) {
        Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
        Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\CurrentUser\Root" | Out-Null
        Write-Host "[OK] Certificate trusted on local system." -ForegroundColor Green
    } else {
        Write-Host "[WARNING] Certificate CER file not found, proceeding with installation..." -ForegroundColor Yellow
    }

    Write-Host ""
    Write-Host "[2/2] Installing FigPin Desktop Application..." -ForegroundColor Yellow
    Add-AppxPackage -Path $msixPath -ForceUpdateFromAnyVersion
    
    Write-Host ""
    Write-Host "======================================================================" -ForegroundColor Green
    Write-Host " [SUCCESS] FigPin v0.1 has been installed on your device!" -ForegroundColor Green
    Write-Host " You can now launch 'FigPin' directly from your Windows Start Menu." -ForegroundColor Green
    Write-Host "======================================================================" -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "[ERROR] Installation failed: $_" -ForegroundColor Red
}

Write-Host ""
Pause
