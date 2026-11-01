# Acer Atlas 8 VRR Fix - extended Intel VRR range for the Acer Predator Atlas 8
# (PA08) internal BOE NS080WUM-LX1 panel.
#
#   Install    EDID override with a lower VRR floor, admin-only runtime in
#              %ProgramData%\AcerAtlasVrr and a sign-in repair task
#   Uninstall  removes everything and restores the saved original Intel state
#   Status     read-only report
#   Repair     run by the sign-in task: keeps Arc Sync on EXCELLENT and the
#              Intel low-FPS solution (LFC) off while the managed EDID is active
[CmdletBinding()]
param(
    [ValidateSet('Install', 'Uninstall', 'Status', 'Repair')]
    [string]$Action = 'Status',
    [ValidateRange(20, 47)]
    [int]$MinimumHz = 28,
    [switch]$NoRestartPrompt
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$version = '1.0.0'
$runtimeDir = Join-Path $env:ProgramData 'AcerAtlasVrr'
$logPath = Join-Path $runtimeDir 'AcerAtlasVrr.log'
$configPath = Join-Path $runtimeDir 'config.json'
$originalStatePath = Join-Path $runtimeDir 'original-state.json'
$taskName = 'Acer Atlas 8 VRR Repair'
$runtimeFiles = @('AcerAtlasVrr.ps1', 'Interop.ps1', 'Launch-Repair.vbs', 'LICENSE-ClawLab.txt')
# Files of the earlier test build that a 1.0.0 install replaces.
$legacyRuntimeFiles = @('Repair-AcerAtlasVrr.ps1', 'ArcSyncControl.ps1', 'Intel-VRR-LFC-Driver-Interface.ps1', 'repair.log')

$panel = [pscustomobject]@{
    Manufacturer = 'BOE'
    ProductCode = '0CFF'
    Name = 'NS080WUM-LX1'
    EdidLength = 256
    PhysicalSha256 = 'F05B01B33B82B9597AE7BB15F5EF43CAEDB9194BF4A81B3008D34E8DBAC6ECCE'
    PhysicalFloorHz = 48
}
# VRR floor bytes: base range descriptor, DisplayID dynamic timing range limits,
# AMD FreeSync VSDB, DisplayID Adaptive-Sync descriptors 1 and 2.
$floorOffsets = @(95, 142, 154, 174, 180)
# The earlier test build patched every floor except Adaptive-Sync descriptor 2.
$testBuildFloorOffsets = @(95, 142, 154, 174)
$displayIdSectionStart = 129
$displayIdChecksumOffset = 254

$profileExcellent = 2
$profileCustom = 7
# Measured on this device before any change: CUSTOM 48-120 Hz, 0/0 us, LFC on.
$factoryState = [pscustomobject]@{
    Source = 'FACTORY_DEFAULT'
    ProfileId = $profileCustom
    MinimumHz = 48.0
    MaximumHz = 120.0
    MaxIncreaseUs = 0
    MaxDecreaseUs = 0
    LowFpsSolutionEnabled = $true
    HighFpsSolutionEnabled = $true
}

. (Join-Path $PSScriptRoot 'Interop.ps1')

# ------------------------------------------------------------------ helpers --

function Write-Log {
    param([Parameter(Mandatory)][string]$Message, [string]$Color = 'Gray')

    Write-Host $Message -ForegroundColor $Color
    if (-not (Test-Path -LiteralPath $runtimeDir -PathType Container)) { return }
    try {
        $lines = @()
        if (Test-Path -LiteralPath $logPath) { $lines = @(Get-Content -LiteralPath $logPath -Tail 499) }
        $lines += "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [$Action] $Message"
        Set-Content -LiteralPath $logPath -Value $lines -Encoding UTF8
    }
    catch {}
}

function Assert-Administrator {
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "$Action requires administrator rights."
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory)][byte[]]$Bytes)

    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '') }
    finally { $sha.Dispose() }
}

function Convert-WmiText {
    param($Values)
    return (-join ($Values | Where-Object { $_ -ne 0 } | ForEach-Object { [char]$_ })).Trim()
}

# -------------------------------------------------------------------- panel --

function Get-PanelDevice {
    $found = @(
        Get-CimInstance -Namespace 'root\wmi' -ClassName 'WmiMonitorID' -ErrorAction Stop | Where-Object {
            (Convert-WmiText $_.ManufacturerName) -eq $panel.Manufacturer -and
            (Convert-WmiText $_.ProductCodeID) -eq $panel.ProductCode -and
            (Convert-WmiText $_.UserFriendlyName) -eq $panel.Name
        }
    )
    if ($found.Count -ne 1) {
        throw "The Acer Atlas 8 panel ($($panel.Manufacturer)$($panel.ProductCode) / $($panel.Name)) was not found exactly once. Found: $($found.Count)."
    }
    $instanceId = [string]$found[0].InstanceName -replace '_\d+$', ''
    $deviceParameters = "Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum\$instanceId\Device Parameters"
    if (-not (Test-Path -LiteralPath $deviceParameters)) {
        throw "The panel registry key is missing: $deviceParameters"
    }
    [pscustomobject]@{
        InstanceId = $instanceId
        DeviceParametersPath = $deviceParameters
        OverridePath = Join-Path $deviceParameters 'EDID_OVERRIDE'
    }
}

function Set-EdidChecksums {
    param([Parameter(Mandatory)][byte[]]$Edid)

    $sum = 0
    for ($i = $displayIdSectionStart; $i -lt $displayIdChecksumOffset; $i++) { $sum += $Edid[$i] }
    $Edid[$displayIdChecksumOffset] = [byte]((256 - ($sum % 256)) % 256)
    foreach ($start in 0, 128) {
        $sum = 0
        for ($i = $start; $i -lt $start + 127; $i++) { $sum += $Edid[$i] }
        $Edid[$start + 127] = [byte]((256 - ($sum % 256)) % 256)
    }
}

function New-EdidVariant {
    param(
        [Parameter(Mandatory)][byte[]]$Physical,
        [Parameter(Mandatory)][int]$FloorHz,
        [int[]]$Offsets = $floorOffsets
    )

    $edid = [byte[]]$Physical.Clone()
    foreach ($offset in $Offsets) { $edid[$offset] = [byte]$FloorHz }
    Set-EdidChecksums -Edid $edid
    return $edid
}

# Maps the SHA-256 of every EDID this tool (or its test build) can write to its floor.
function Get-KnownVariants {
    param([Parameter(Mandatory)][byte[]]$Physical)

    $known = @{}
    foreach ($floor in 10..47) {
        $known[(Get-Sha256 (New-EdidVariant -Physical $Physical -FloorHz $floor))] =
            [pscustomobject]@{ FloorHz = $floor; TestBuild = $false }
        $known[(Get-Sha256 (New-EdidVariant -Physical $Physical -FloorHz $floor -Offsets $testBuildFloorOffsets))] =
            [pscustomobject]@{ FloorHz = $floor; TestBuild = $true }
    }
    return $known
}

function Get-EdidState {
    param([Parameter(Mandatory)][object]$Device)

    $reported = [byte[]](Get-ItemPropertyValue -LiteralPath $Device.DeviceParametersPath -Name 'EDID')
    if ($reported.Length -ne $panel.EdidLength) {
        throw "Unexpected EDID length $($reported.Length); expected $($panel.EdidLength)."
    }
    $reportedHash = Get-Sha256 $reported

    # With an override loaded Windows reports the overridden EDID. Restoring
    # every floor byte to 48 must then give back the exact physical EDID.
    $physical = if ($reportedHash -eq $panel.PhysicalSha256) {
        $reported
    }
    else {
        New-EdidVariant -Physical $reported -FloorHz $panel.PhysicalFloorHz
    }
    if ((Get-Sha256 $physical) -ne $panel.PhysicalSha256) {
        throw "Unsupported panel EDID $reportedHash. Only the validated Acer Atlas 8 panel and this tool's own variants are accepted."
    }
    $known = Get-KnownVariants -Physical $physical

    $reportedVariant = $null
    if ($reportedHash -ne $panel.PhysicalSha256) {
        $reportedVariant = $known[$reportedHash]
        if ($null -eq $reportedVariant) {
            throw "The active EDID $reportedHash is not one of this tool's variants. Remove other EDID tools (e.g. CRU reset-all) first."
        }
    }

    $overrideKind = 'NONE'
    $overrideHash = $null
    $overrideVariant = $null
    if (Test-Path -LiteralPath $Device.OverridePath) {
        $overrideKind = 'FOREIGN'
        $values = Get-ItemProperty -LiteralPath $Device.OverridePath
        $names = @($values.PSObject.Properties.Name | Where-Object { $_ -notlike 'PS*' })
        if ((@($names | Sort-Object) -join ',') -eq '0,1') {
            $override = [byte[]]([byte[]]$values.'0' + [byte[]]$values.'1')
            $overrideHash = Get-Sha256 $override
            $overrideVariant = $known[$overrideHash]
            if ($null -ne $overrideVariant) { $overrideKind = 'OURS' }
        }
    }

    [pscustomobject]@{
        Physical = $physical
        ReportedSha256 = $reportedHash
        ReportedVariant = $reportedVariant
        OverrideKind = $overrideKind
        OverrideSha256 = $overrideHash
        OverrideVariant = $overrideVariant
    }
}

function Format-Variant {
    param($Variant)
    if ($null -eq $Variant) { return 'factory 48-120 Hz' }
    $suffix = if ($Variant.TestBuild) { ' (test build)' } else { '' }
    return "$($Variant.FloorHz)-120 Hz$suffix"
}

# ------------------------------------------------------------ intel driver --

function Get-ArcSyncTarget {
    param([int]$WaitSeconds = 0)

    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    do {
        try {
            $targets = @([ClawLab.VrrFix.ArcSyncControl]::Query() | Where-Object {
                    $_.MonitorResult -eq 0 -and $_.ProfileResult -eq 0 -and $_.Supported
                })
            if ($targets.Count -eq 1) { return $targets[0] }
            $problem = "Expected exactly one Intel Arc Sync display, found $($targets.Count). Disconnect external displays."
        }
        catch {
            $problem = $_.Exception.Message
        }
        if ((Get-Date) -lt $deadline) { Start-Sleep -Seconds 3 }
    } while ((Get-Date) -lt $deadline)
    throw $problem
}

function Format-Profile {
    param([Parameter(Mandatory)][object]$Target)
    return ('{0} {1}-{2} Hz ({3}/{4} us)' -f $Target.ProfileName, $Target.ActiveMinimumHz,
        $Target.ActiveMaximumHz, $Target.ActiveMaxIncreaseUs, $Target.ActiveMaxDecreaseUs)
}

function Set-ArcSyncProfile {
    param(
        [Parameter(Mandatory)][int]$ProfileId,
        [float]$MinimumHz = 0,
        [float]$MaximumHz = 0,
        [uint32]$MaxIncreaseUs = 0,
        [uint32]$MaxDecreaseUs = 0
    )

    $target = Get-ArcSyncTarget
    $result = [ClawLab.VrrFix.ArcSyncControl]::SetProfile($target.AdapterIndex, $target.DisplayIndex,
        $target.MonitorMinimumHz, $target.MonitorMaximumHz, $ProfileId,
        $MinimumHz, $MaximumHz, $MaxIncreaseUs, $MaxDecreaseUs)
    if ($result -ne 0) {
        throw ('Intel ctlSetIntelArcSyncProfile failed with 0x{0:X8}.' -f [uint32]$result)
    }
    Start-Sleep -Milliseconds 1500
    $after = Get-ArcSyncTarget
    if ($after.ProfileId -ne $ProfileId) {
        throw "Intel kept profile $(Format-Profile $after) instead of profile $ProfileId."
    }
    return $after
}

function Get-LfcState {
    $state = [ClawLab.IntelVrr.DirectVrrEscape]::Invoke(0)
    if ($state.NtStatus -ne 0) {
        throw ('Intel private VRR escape failed: 0x{0:X8}' -f $state.NtStatus)
    }
    return $state
}

function Set-LfcFlag {
    param(
        [Parameter(Mandatory)][ValidateSet('LowFps', 'HighFps')][string]$Solution,
        [Parameter(Mandatory)][bool]$Enabled
    )

    $operation = switch ($Solution) {
        'LowFps' { if ($Enabled) { 3 } else { 4 } }
        'HighFps' { if ($Enabled) { 5 } else { 6 } }
    }
    $result = [ClawLab.IntelVrr.DirectVrrEscape]::Invoke([uint32]$operation)
    if ($result.NtStatus -ne 0) {
        throw ('Intel private VRR escape failed: 0x{0:X8}' -f $result.NtStatus)
    }
    $after = Get-LfcState
    $actual = if ($Solution -eq 'LowFps') { $after.LowFpsSolutionEnabled } else { $after.HighFpsSolutionEnabled }
    if ($actual -ne $Enabled) {
        throw "Intel $Solution solution did not change to $Enabled."
    }
}

# ------------------------------------------------------------------ runtime --

function Read-JsonFile {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Write-JsonFile {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][object]$Value)
    $temp = "$Path.tmp"
    $Value | ConvertTo-Json | Set-Content -LiteralPath $temp -Encoding UTF8
    Move-Item -LiteralPath $temp -Destination $Path -Force
}

function Initialize-RuntimeDirectory {
    New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
    # Administrators and SYSTEM full control, users read-only: the task runs
    # these scripts elevated, so standard users must not be able to change them.
    & icacls.exe $runtimeDir /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Setting permissions on $runtimeDir failed." }
}

function Install-RuntimeFiles {
    $source = (Resolve-Path -LiteralPath $PSScriptRoot).Path.TrimEnd('\')
    if ($source -ne $runtimeDir.TrimEnd('\')) {
        foreach ($file in $runtimeFiles) {
            Copy-Item -LiteralPath (Join-Path $source $file) -Destination $runtimeDir -Force
        }
    }
    # Files from a downloaded ZIP carry the internet zone mark, which can make
    # the sign-in task show a security prompt. Clear it on the installed copies.
    foreach ($file in $runtimeFiles) {
        Unblock-File -LiteralPath (Join-Path $runtimeDir $file) -ErrorAction SilentlyContinue
    }
    foreach ($file in $legacyRuntimeFiles) {
        $path = Join-Path $runtimeDir $file
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
}

function Register-RepairTask {
    $user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $taskAction = New-ScheduledTaskAction -Execute 'wscript.exe' -Argument "`"$(Join-Path $runtimeDir 'Launch-Repair.vbs')`""
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
    $trigger.Delay = 'PT10S'
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit (New-TimeSpan -Minutes 5)
    Register-ScheduledTask -TaskName $taskName -Action $taskAction -Trigger $trigger -Principal $principal `
        -Settings $settings -Force `
        -Description 'Acer Atlas 8 VRR Fix: keeps Intel Arc Sync on EXCELLENT and LFC off for the extended VRR range.' | Out-Null
    return $user
}

function Request-Restart {
    param([Parameter(Mandatory)][string]$Reason)

    Write-Log "Restart Windows to $Reason." 'Yellow'
    if ($NoRestartPrompt) { return }
    $answer = Read-Host 'Restart now? (y/N)'
    if ($answer -match '^(y|j)') {
        Write-Log 'Restarting in 10 seconds.' 'Yellow'
        & shutdown.exe /r /t 10 /c 'Acer Atlas 8 VRR Fix'
    }
}

# ------------------------------------------------------------------ actions --

function Invoke-Install {
    Assert-Administrator
    Initialize-IntelVrrInterop
    Write-Host "Acer Atlas 8 VRR Fix $version - install $MinimumHz-120 Hz" -ForegroundColor Cyan

    $device = Get-PanelDevice
    $edid = Get-EdidState -Device $device
    if ($edid.OverrideKind -eq 'FOREIGN') {
        throw 'An EDID override from another tool exists for this panel. Remove it first (CRU: run reset-all.exe and restart). Nothing was changed.'
    }
    $target = Get-ArcSyncTarget
    $lfc = Get-LfcState

    Initialize-RuntimeDirectory
    Write-Log "Install $version started: floor $MinimumHz Hz, active EDID $(Format-Variant $edid.ReportedVariant), Intel $(Format-Profile $target), LFC low=$($lfc.LowFpsSolutionEnabled) high=$($lfc.HighFpsSolutionEnabled)."

    # Save the original Intel state once. If an override of ours is already
    # present the current state is not original, so the measured factory
    # defaults are saved instead.
    if (-not (Test-Path -LiteralPath $originalStatePath)) {
        $original = if ($edid.OverrideKind -eq 'NONE' -and $null -eq $edid.ReportedVariant) {
            [pscustomobject]@{
                Source = 'MEASURED'
                ProfileId = $target.ProfileId
                MinimumHz = $target.ActiveMinimumHz
                MaximumHz = $target.ActiveMaximumHz
                MaxIncreaseUs = $target.ActiveMaxIncreaseUs
                MaxDecreaseUs = $target.ActiveMaxDecreaseUs
                LowFpsSolutionEnabled = $lfc.LowFpsSolutionEnabled
                HighFpsSolutionEnabled = $lfc.HighFpsSolutionEnabled
            }
        }
        else { $factoryState }
        Write-JsonFile -Path $originalStatePath -Value $original
        Write-Log "Saved original Intel state ($($original.Source))."
    }

    $variant = New-EdidVariant -Physical $edid.Physical -FloorHz $MinimumHz
    $variantHash = Get-Sha256 $variant
    if ($edid.OverrideSha256 -ne $variantHash) {
        New-Item -Path $device.OverridePath -Force | Out-Null
        New-ItemProperty -LiteralPath $device.OverridePath -Name '0' -PropertyType Binary -Value ([byte[]]$variant[0..127]) -Force | Out-Null
        New-ItemProperty -LiteralPath $device.OverridePath -Name '1' -PropertyType Binary -Value ([byte[]]$variant[128..255]) -Force | Out-Null
        $readBack = Get-EdidState -Device $device
        if ($readBack.OverrideSha256 -ne $variantHash) {
            throw 'The EDID override read-back does not match.'
        }
        Write-Log "EDID override $MinimumHz-120 Hz written ($variantHash)."
    }
    else {
        Write-Log "EDID override $MinimumHz-120 Hz already present."
    }

    Install-RuntimeFiles
    Write-JsonFile -Path $configPath -Value ([pscustomobject]@{
            Version = $version
            MinimumHz = $MinimumHz
            EdidSha256 = $variantHash
            InstanceId = $device.InstanceId
            InstalledAt = (Get-Date).ToString('o')
        })
    $user = Register-RepairTask
    Write-Log "Sign-in task '$taskName' registered for $user."

    if ($edid.ReportedSha256 -eq $variantHash) {
        Invoke-Repair
        Write-Log "Installed. VRR range $MinimumHz-120 Hz is active." 'Green'
    }
    else {
        Write-Log "Installed. The $MinimumHz-120 Hz range becomes active after a restart; the sign-in task then sets the Intel profile." 'Green'
        Request-Restart -Reason "load the $MinimumHz-120 Hz EDID"
    }
}

function Invoke-Repair {
    Initialize-IntelVrrInterop
    $config = Read-JsonFile -Path $configPath
    if ($null -eq $config) { throw "Not installed: $configPath is missing." }
    $floor = [int]$config.MinimumHz

    # The Intel driver can still be initialising right after sign-in.
    $target = Get-ArcSyncTarget -WaitSeconds 90
    $edid = Get-EdidState -Device (Get-PanelDevice)
    if ($edid.ReportedSha256 -ne [string]$config.EdidSha256) {
        Write-Log "SKIP active EDID is $(Format-Variant $edid.ReportedVariant), not the installed $floor-120 Hz (restart pending, driver reinstall or override removed). Nothing changed." 'Yellow'
        return
    }
    if ([Math]::Abs($target.MonitorMinimumHz - $floor) -gt 0.1) {
        Write-Log "SKIP Intel reports a $($target.MonitorMinimumHz)-$($target.MonitorMaximumHz) Hz panel range, expected floor $floor. Nothing changed." 'Yellow'
        return
    }

    $changes = @()
    $profileOk = $target.ProfileId -eq $profileExcellent -and
        [Math]::Abs($target.ActiveMinimumHz - $floor) -le 0.1 -and
        [Math]::Abs($target.ActiveMaximumHz - 120) -le 0.1
    if (-not $profileOk) {
        $before = Format-Profile $target
        $after = Set-ArcSyncProfile -ProfileId $profileExcellent
        if ([Math]::Abs($after.ActiveMinimumHz - $floor) -gt 0.1) {
            throw "EXCELLENT was set but its range is $($after.ActiveMinimumHz)-$($after.ActiveMaximumHz) Hz."
        }
        $changes += "profile $before -> $(Format-Profile $after)"
    }
    if ((Get-LfcState).LowFpsSolutionEnabled) {
        Set-LfcFlag -Solution LowFps -Enabled $false
        $changes += 'LFC on -> off'
    }

    if ($changes.Count -eq 0) { Write-Log "OK EXCELLENT $floor-120 Hz, LFC off; nothing to repair." 'Green' }
    else { Write-Log ("REPAIRED " + ($changes -join '; ')) 'Green' }
}

function Invoke-Uninstall {
    Assert-Administrator
    Initialize-IntelVrrInterop
    Write-Host "Acer Atlas 8 VRR Fix $version - uninstall" -ForegroundColor Cyan

    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        Write-Log "Sign-in task '$taskName' removed."
    }

    $restartNeeded = $false
    $device = Get-PanelDevice
    $edid = Get-EdidState -Device $device
    switch ($edid.OverrideKind) {
        'OURS' {
            Remove-Item -LiteralPath $device.OverridePath -Recurse -Force
            Write-Log 'EDID override removed.'
        }
        'FOREIGN' { Write-Log 'An EDID override from another tool exists; it was left untouched.' 'Yellow' }
    }
    if ($null -ne $edid.ReportedVariant) { $restartNeeded = $true }

    $original = Read-JsonFile -Path $originalStatePath
    if ($null -eq $original) {
        $original = $factoryState
        Write-Log 'No saved original state found; restoring the factory defaults.' 'Yellow'
    }
    $restored = if ([int]$original.ProfileId -eq $profileCustom) {
        Set-ArcSyncProfile -ProfileId $profileCustom -MinimumHz ([float]$original.MinimumHz) -MaximumHz ([float]$original.MaximumHz) `
            -MaxIncreaseUs ([uint32]$original.MaxIncreaseUs) -MaxDecreaseUs ([uint32]$original.MaxDecreaseUs)
    }
    else {
        Set-ArcSyncProfile -ProfileId ([int]$original.ProfileId)
    }
    Write-Log "Intel profile restored: $(Format-Profile $restored)."
    $lfc = Get-LfcState
    if ($lfc.LowFpsSolutionEnabled -ne [bool]$original.LowFpsSolutionEnabled) {
        Set-LfcFlag -Solution LowFps -Enabled ([bool]$original.LowFpsSolutionEnabled)
    }
    if ($lfc.HighFpsSolutionEnabled -ne [bool]$original.HighFpsSolutionEnabled) {
        Set-LfcFlag -Solution HighFps -Enabled ([bool]$original.HighFpsSolutionEnabled)
    }
    Write-Log "LFC restored: low=$([bool]$original.LowFpsSolutionEnabled) high=$([bool]$original.HighFpsSolutionEnabled)."

    if (Test-Path -LiteralPath $runtimeDir) {
        Remove-Item -LiteralPath $runtimeDir -Recurse -Force
    }
    Write-Host "Removed $runtimeDir."
    Write-Host 'Uninstalled.' -ForegroundColor Green
    if ($restartNeeded) { Request-Restart -Reason 'load the factory 48-120 Hz EDID' }
}

function Invoke-Status {
    Initialize-IntelVrrInterop
    Write-Host "Acer Atlas 8 VRR Fix $version - status" -ForegroundColor Cyan

    $device = Get-PanelDevice
    $edid = Get-EdidState -Device $device
    $override = switch ($edid.OverrideKind) {
        'NONE' { 'none' }
        'OURS' { Format-Variant $edid.OverrideVariant }
        'FOREIGN' { 'FOREIGN (another tool)' }
    }
    $pending = switch ($edid.OverrideKind) {
        'OURS' { $edid.OverrideSha256 -ne $edid.ReportedSha256 }
        'NONE' { $null -ne $edid.ReportedVariant }
        default { $false }
    }
    $target = Get-ArcSyncTarget
    $lfc = Get-LfcState
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    $config = Read-JsonFile -Path $configPath

    $installed = 'no'
    if ($null -ne $config) {
        $installedVersion = if ($config.PSObject.Properties.Name -contains 'Version') { $config.Version } else { 'test build' }
        $installed = "version $installedVersion, floor $($config.MinimumHz) Hz"
    }
    $taskText = 'not registered'
    if ($null -ne $task) {
        $info = Get-ScheduledTaskInfo -TaskName $taskName
        $taskText = "registered, last run $($info.LastRunTime), result $($info.LastTaskResult)"
    }
    $onOff = @{ $true = 'on'; $false = 'off' }

    $rows = [ordered]@{
        'Panel' = "$($panel.Manufacturer)$($panel.ProductCode) $($panel.Name) ($($device.InstanceId))"
        'Active EDID' = Format-Variant $edid.ReportedVariant
        'EDID override' = $override
        'Restart pending' = $pending
        'Intel panel range' = "$($target.MonitorMinimumHz)-$($target.MonitorMaximumHz) Hz"
        'Intel profile' = Format-Profile $target
        'LFC (low-FPS)' = $onOff[[bool]$lfc.LowFpsSolutionEnabled]
        'High-FPS solution' = $onOff[[bool]$lfc.HighFpsSolutionEnabled]
        'Installed' = $installed
        'Sign-in task' = $taskText
    }
    $rows.GetEnumerator() | ForEach-Object { '{0,-18} {1}' -f $_.Key, $_.Value } | Write-Host

    if (Test-Path -LiteralPath $logPath) {
        Write-Host "`nRecent log ($logPath):"
        Get-Content -LiteralPath $logPath -Tail 5 | Write-Host
    }
}

try {
    switch ($Action) {
        'Install' { Invoke-Install }
        'Uninstall' { Invoke-Uninstall }
        'Status' { Invoke-Status }
        'Repair' { Invoke-Repair }
    }
}
catch {
    Write-Log "ERROR $($_.Exception.Message)" 'Red'
    exit 1
}
