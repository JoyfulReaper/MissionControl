<#
.SYNOPSIS
Publishes MissionControl.Mobile for Android and installs it on a connected phone.

.DESCRIPTION
Run this script from the repository. It:

1. Finds the MissionControl.Mobile project.
2. Finds ADB and a connected Android device.
3. Increments ApplicationVersion and, by default, the patch component of
   ApplicationDisplayVersion.
4. Publishes a signed Release APK using the existing Mission Control keystore.
5. Installs the APK with `adb install -r`, preserving app data and SecureStorage.

The project version update is retained only after a successful installation.

.EXAMPLE
.\scripts\Update-MissionControlPhone.ps1

Automatically changes 1.0.2 to 1.0.3, increments the numeric build version,
publishes, and installs the update.

.EXAMPLE
.\scripts\Update-MissionControlPhone.ps1 -Version 1.1.0

Sets the displayed version to 1.1.0, increments the numeric build version,
publishes, and installs the update.

.EXAMPLE
.\scripts\Update-MissionControlPhone.ps1 -DeviceSerial 192.168.1.50:5555

Targets a specific ADB device when more than one device is connected.

.EXAMPLE
.\scripts\Update-MissionControlPhone.ps1 -SkipVersionBump

Rebuilds and reinstalls the versions currently stored in the project file.
#>

[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?$')]
    [string]$Version,

    [Parameter()]
    [string]$DeviceSerial,

    [Parameter()]
    [string]$AdbPath =
        "${env:ProgramFiles(x86)}\Android\android-sdk\platform-tools\adb.exe",

    [Parameter()]
    [string]$KeystorePath =
        (Join-Path $env:USERPROFILE ".missioncontrol\missioncontrol.keystore"),

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$SigningAlias = "missioncontrol",

    [Parameter()]
    [switch]$SkipVersionBump
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)

    Write-Host
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$FailureMessage
    )

    & $Command @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage Exit code: $LASTEXITCODE"
    }
}

function Get-ProjectDocument {
    param([Parameter(Mandatory)][string]$Path)

    $document = [System.Xml.XmlDocument]::new()
    $document.PreserveWhitespace = $true
    $document.Load($Path)
    return $document
}

function Get-ProjectElement {
    param(
        [Parameter(Mandatory)][System.Xml.XmlDocument]$Document,
        [Parameter(Mandatory)][string]$ElementName
    )

    [System.Xml.XmlNodeList]$elements = $Document.SelectNodes(
        "//*[local-name()='$ElementName']")

    if ($elements.Count -ne 1) {
        throw "Expected exactly one <$ElementName> in the MAUI project file; found $($elements.Count)."
    }

    return $elements[0]
}

function Get-ProjectValue {
    param(
        [Parameter(Mandatory)][System.Xml.XmlDocument]$Document,
        [Parameter(Mandatory)][string]$ElementName
    )

    return (Get-ProjectElement `
        -Document $Document `
        -ElementName $ElementName).InnerText.Trim()
}

function Set-ProjectValue {
    param(
        [Parameter(Mandatory)][System.Xml.XmlDocument]$Document,
        [Parameter(Mandatory)][string]$ElementName,
        [Parameter(Mandatory)][string]$Value
    )

    (Get-ProjectElement `
        -Document $Document `
        -ElementName $ElementName).InnerText = $Value
}

function Save-ProjectDocument {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][System.Xml.XmlDocument]$Document,
        [Parameter(Mandatory)][bool]$HasUtf8Bom
    )

    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Encoding = [System.Text.UTF8Encoding]::new($HasUtf8Bom)
    $settings.Indent = $false
    $settings.NewLineHandling =
        [System.Xml.NewLineHandling]::None

    $writer = [System.Xml.XmlWriter]::Create($Path, $settings)

    try {
        $Document.Save($writer)
    }
    finally {
        $writer.Dispose()
    }
}

function Get-NextDisplayVersion {
    param([Parameter(Mandatory)][string]$CurrentVersion)

    $parsed = [version]$CurrentVersion
    $patch = if ($parsed.Build -ge 0) {
        $parsed.Build + 1
    }
    else {
        1
    }

    return "$($parsed.Major).$($parsed.Minor).$patch"
}

function Get-ConnectedDevice {
    param(
        [Parameter(Mandatory)][string]$Adb,
        [string]$RequestedSerial
    )

    $deviceOutput = & $Adb devices

    if ($LASTEXITCODE -ne 0) {
        throw "Unable to query connected Android devices."
    }

    $availableDevices = @(
        foreach ($line in $deviceOutput) {
            if ($line -match "^(?<serial>\S+)\s+device$") {
                $Matches["serial"]
            }
        }
    )

    $unauthorizedDevices = @(
        foreach ($line in $deviceOutput) {
            if ($line -match "^(?<serial>\S+)\s+unauthorized$") {
                $Matches["serial"]
            }
        }
    )

    if ($unauthorizedDevices.Count -gt 0) {
        throw "Android device is unauthorized. Unlock the phone, approve the USB debugging prompt, and run the script again. Device: $($unauthorizedDevices -join ', ')"
    }

    if ($RequestedSerial) {
        if ($availableDevices -notcontains $RequestedSerial) {
            throw "Requested device '$RequestedSerial' is not connected. Connected devices: $($availableDevices -join ', ')"
        }

        return $RequestedSerial
    }

    if ($availableDevices.Count -eq 0) {
        throw "No authorized Android device was found. Connect the phone with USB debugging enabled and approve the authorization prompt."
    }

    if ($availableDevices.Count -gt 1) {
        throw "Multiple Android devices are connected. Run the script again with -DeviceSerial. Connected devices: $($availableDevices -join ', ')"
    }

    return $availableDevices[0]
}

function Get-InstalledBuildNumber {
    param(
        [Parameter(Mandatory)][string]$Adb,
        [Parameter(Mandatory)][string]$Serial,
        [Parameter(Mandatory)][string]$PackageId
    )

    $packageInfo =
        & $Adb -s $Serial shell dumpsys package $PackageId 2>$null

    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    foreach ($line in $packageInfo) {
        if ($line -match "versionCode=(?<version>\d+)") {
            return [int]$Matches["version"]
        }
    }

    return $null
}

if ($SkipVersionBump -and $Version) {
    throw "-Version cannot be combined with -SkipVersionBump."
}

$scriptDirectory = Split-Path -Parent $PSCommandPath
$repoRoot = Split-Path -Parent $scriptDirectory
$projectPath = Join-Path `
    $repoRoot `
    "MissionControl.Mobile\MissionControl.Mobile.csproj"
$publishOutputPath = Join-Path `
    ([System.IO.Path]::GetTempPath()) `
    "mission-control-mobile-$([Guid]::NewGuid().ToString('N'))"

Write-Step "Validating tools and repository paths"

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Mission Control MAUI project was not found: $projectPath"
}

if (-not (Test-Path -LiteralPath $AdbPath -PathType Leaf)) {
    throw "ADB was not found: $AdbPath"
}

if (-not (Test-Path -LiteralPath $KeystorePath -PathType Leaf)) {
    throw "Android signing keystore was not found: $KeystorePath"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet was not found on PATH."
}

Write-Step "Selecting connected Android device"

$selectedDevice = Get-ConnectedDevice `
    -Adb $AdbPath `
    -RequestedSerial $DeviceSerial

Write-Host "Device: $selectedDevice"

$originalProjectBytes = [System.IO.File]::ReadAllBytes($projectPath)
$hasUtf8Bom =
    $originalProjectBytes.Length -ge 3 -and
    $originalProjectBytes[0] -eq 0xEF -and
    $originalProjectBytes[1] -eq 0xBB -and
    $originalProjectBytes[2] -eq 0xBF
$projectDocument = Get-ProjectDocument -Path $projectPath
$currentDisplayVersion = Get-ProjectValue `
    -Document $projectDocument `
    -ElementName "ApplicationDisplayVersion"
$currentBuildNumberText = Get-ProjectValue `
    -Document $projectDocument `
    -ElementName "ApplicationVersion"
$packageId = Get-ProjectValue `
    -Document $projectDocument `
    -ElementName "ApplicationId"

$currentBuildNumber = 0

if (-not [int]::TryParse(
        $currentBuildNumberText,
        [ref]$currentBuildNumber)) {
    throw "ApplicationVersion must be an integer. Current value: $currentBuildNumberText"
}

$installedBuildNumber = Get-InstalledBuildNumber `
    -Adb $AdbPath `
    -Serial $selectedDevice `
    -PackageId $packageId

if ($null -ne $installedBuildNumber) {
    Write-Host "Installed build: $installedBuildNumber"
}
else {
    Write-Host "The app is not currently installed, or its installed build could not be read."
}

$versionUpdated = $false
$installationSucceeded = $false
$securePassword = $null
$passwordPointer = [IntPtr]::Zero

try {
    if (-not $SkipVersionBump) {
        $nextDisplayVersion = if ($Version) {
            $Version
        }
        else {
            Get-NextDisplayVersion `
                -CurrentVersion $currentDisplayVersion
        }

        $highestKnownBuild = $currentBuildNumber

        if ($null -ne $installedBuildNumber) {
            $highestKnownBuild = [Math]::Max(
                $highestKnownBuild,
                $installedBuildNumber)
        }

        if ($highestKnownBuild -ge 2100000000) {
            throw "The Android build number cannot be incremented beyond 2100000000."
        }

        $nextBuildNumber = $highestKnownBuild + 1

        Write-Step "Updating version"

        Write-Host "Display version: $currentDisplayVersion -> $nextDisplayVersion"
        Write-Host "Build number:    $currentBuildNumber -> $nextBuildNumber"

        Set-ProjectValue `
            -Document $projectDocument `
            -ElementName "ApplicationDisplayVersion" `
            -Value $nextDisplayVersion
        Set-ProjectValue `
            -Document $projectDocument `
            -ElementName "ApplicationVersion" `
            -Value $nextBuildNumber

        Save-ProjectDocument `
            -Path $projectPath `
            -Document $projectDocument `
            -HasUtf8Bom $hasUtf8Bom

        $versionUpdated = $true
    }
    else {
        Write-Step "Keeping current project version"

        Write-Host "Display version: $currentDisplayVersion"
        Write-Host "Build number:    $currentBuildNumber"

        if ($null -ne $installedBuildNumber -and
            $currentBuildNumber -lt $installedBuildNumber) {
            Write-Warning "The project build number is lower than the installed build. Android may reject the installation as a downgrade."
        }
    }

    Write-Step "Reading Android signing password"

    $securePassword = Read-Host `
        "Android signing password" `
        -AsSecureString
    $passwordPointer =
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR(
            $securePassword)

    $env:MISSIONCONTROL_ANDROID_SIGNING_PASSWORD =
        [Runtime.InteropServices.Marshal]::PtrToStringBSTR(
            $passwordPointer)

    Write-Step "Publishing signed Android APK"

    $publishArguments = @(
        "publish"
        $projectPath
        "-f"
        "net10.0-android"
        "-c"
        "Release"
        "--output"
        $publishOutputPath
        "-p:AndroidPackageFormats=apk"
        "-p:AndroidKeyStore=true"
        "-p:AndroidSigningKeyStore=$KeystorePath"
        "-p:AndroidSigningKeyAlias=$SigningAlias"
        "-p:AndroidSigningKeyPass=env:MISSIONCONTROL_ANDROID_SIGNING_PASSWORD"
        "-p:AndroidSigningStorePass=env:MISSIONCONTROL_ANDROID_SIGNING_PASSWORD"
    )

    Invoke-CheckedCommand `
        -Command "dotnet" `
        -Arguments $publishArguments `
        -FailureMessage "Android publish failed."

    Write-Step "Finding signed APK"

    $signedApks = @(
        Get-ChildItem `
            -LiteralPath $publishOutputPath `
            -Filter "*Signed.apk" `
            -File `
            -Recurse
    )

    if ($signedApks.Count -ne 1) {
        throw "Expected one signed APK under '$publishOutputPath'; found $($signedApks.Count)."
    }

    $apk = $signedApks[0]
    Write-Host "APK: $($apk.FullName)"

    Write-Step "Installing update on $selectedDevice"

    $installOutput =
        & $AdbPath -s $selectedDevice install -r $apk.FullName 2>&1
    $installExitCode = $LASTEXITCODE

    $installOutput | ForEach-Object { Write-Host $_ }

    if ($installExitCode -ne 0 -or
        -not ($installOutput -match "^Success$")) {
        throw "ADB installation failed. Exit code: $installExitCode"
    }

    $installationSucceeded = $true

    Write-Host
    Write-Host "Mission Control was updated successfully." `
        -ForegroundColor Green
    Write-Host "Package: $packageId"
    Write-Host "Device:  $selectedDevice"
}
finally {
    Remove-Item `
        Env:MISSIONCONTROL_ANDROID_SIGNING_PASSWORD `
        -ErrorAction SilentlyContinue

    if ($passwordPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR(
            $passwordPointer)
    }

    $securePassword = $null

    if ($versionUpdated -and -not $installationSucceeded) {
        [System.IO.File]::WriteAllBytes(
            $projectPath,
            $originalProjectBytes)

        Write-Warning "The project version was restored because the update did not complete."
    }

    if (Test-Path -LiteralPath $publishOutputPath) {
        Remove-Item `
            -LiteralPath $publishOutputPath `
            -Recurse `
            -Force
    }
}
