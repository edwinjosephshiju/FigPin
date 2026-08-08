# FigPin Local Sideloading Installer Script
# Self-elevates to administrator, extracts & trusts certificate from MSIX container into TrustedPeople and TrustedRoot stores, enables sideloading, and installs FigPin App

$msixPath = "$PSScriptRoot\edwinjoseph.FigPin_0.1.0.0_x64.msix"
$cerPath  = "$PSScriptRoot\FigPinStoreDev.cer"

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "             FigPin Studio - Local MSIX Package Installer" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host ""

# Unblock files downloaded from Web (remove Zone.Identifier Mark-Of-The-Web)
Get-ChildItem -Path $PSScriptRoot -Recurse | Unblock-File -ErrorAction SilentlyContinue

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

# STEP 1: Always extract .cer certificate directly from the MSIX package FIRST (does not require admin)
if (-not (Test-Path $cerPath)) {
    Write-Host "[INFO] Extracting public signature certificate from MSIX package container..." -ForegroundColor Yellow
    try {
        $signerCert = (Get-AuthenticodeSignature -FilePath $msixPath).SignerCertificate
        if ($null -ne $signerCert) {
            $bytes = $signerCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
            [System.IO.File]::WriteAllBytes($cerPath, $bytes)
            Write-Host "[OK] Certificate 'FigPinStoreDev.cer' generated successfully!" -ForegroundColor Green
        } else {
            Write-Host "[WARNING] Could not read certificate from MSIX signature." -ForegroundColor Yellow
        }
    } catch {
        Write-Host "[WARNING] Failed to extract certificate: $_" -ForegroundColor Yellow
    }
}

# STEP 2: Elevate to Administrator if needed
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "[INFO] Requesting Administrator elevation to trust certificate..." -ForegroundColor Yellow
    Start-Process powershell -Verb RunAs -WorkingDirectory "$PSScriptRoot" -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    Exit
}

# STEP 3: Import Certificate & Enable Sideloading (Elevated Context)
try {
    Write-Host "[1/3] Enabling Windows App Sideloading..." -ForegroundColor Yellow
    try {
        if (-not (Test-Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock")) {
            New-Item -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" -Force | Out-Null
        }
        Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" -Name "AllowAllTrustedApps" -Value 1 -Force -ErrorAction SilentlyContinue
        Write-Host "[OK] Windows Sideloading enabled." -ForegroundColor Green
    } catch { }

    Write-Host "[2/3] Importing Developer Certificate into Trusted People & Trusted Root stores..." -ForegroundColor Yellow
    if (Test-Path $cerPath) {
        Unblock-File -Path $cerPath -ErrorAction SilentlyContinue
        
        # Import to TrustedPeople (Required for Windows AppX/MSIX package deployment)
        Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null
        Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" | Out-Null
        certutil -addstore -f "TrustedPeople" "$cerPath" | Out-Null

        # Import to Trusted Root Certification Authorities
        Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
        Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\CurrentUser\Root" | Out-Null
        certutil -addstore -f "Root" "$cerPath" | Out-Null
        
        Write-Host "[OK] Certificate trusted in Trusted People & Trusted Root stores." -ForegroundColor Green
    } else {
        Write-Host "[ERROR] Certificate file missing: $cerPath" -ForegroundColor Red
        Pause
        Exit
    }

    Write-Host ""
    Write-Host "[3/3] Installing FigPin Desktop Application..." -ForegroundColor Yellow
    
    # Remove previous build if installed to prevent 0x80073CFB (same version, updated content)
    $existingPackage = Get-AppxPackage -Name "edwinjoseph.FigPin" -ErrorAction SilentlyContinue
    if ($null -ne $existingPackage) {
        Write-Host "[INFO] Removing previous build of FigPin..." -ForegroundColor Yellow
        Remove-AppxPackage -Package $existingPackage.PackageFullName -ErrorAction SilentlyContinue
    }

    Unblock-File -Path $msixPath -ErrorAction SilentlyContinue
    Add-AppxPackage -Path $msixPath -ForceUpdateFromAnyVersion -ErrorAction Stop
    
    Write-Host ""
    Write-Host "======================================================================" -ForegroundColor Green
    Write-Host " [SUCCESS] FigPin v0.1 has been installed on your device!" -ForegroundColor Green
    Write-Host " You can now launch 'FigPin' directly from your Windows Start Menu." -ForegroundColor Green
    Write-Host "======================================================================" -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "[ERROR] Installation failed: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "If reinstallation was blocked, try running: Remove-AppxPackage (Get-AppxPackage *FigPin*).PackageFullName" -ForegroundColor Yellow
}

Write-Host ""
Pause
