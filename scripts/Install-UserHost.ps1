param(
    [string]$MsiPath,
    [string]$InstallRoot,
    [switch]$Quiet,
    [switch]$UserSetupOnly
)

$ErrorActionPreference = 'Stop'
function Resolve-InstallRoot([string]$Path) {
    # Windows PowerShell 5.1 runs on .NET Framework, which lacks IsPathFullyQualified.
    # IsPathRooted alone would also accept drive-relative and root-relative paths.
    $isDriveAbsolute = $Path -match '^[A-Za-z]:[\\/]'
    $isUncAbsolute = $Path -match '^\\\\[^\\/:*?"<>|]+\\[^\\/:*?"<>|]+(?:\\|$)'
    if ((-not $isDriveAbsolute -and -not $isUncAbsolute) -or $Path -match '["\r\n]') {
        throw 'InstallRoot must be an absolute installation directory without quotes or newlines.'
    }
    return [IO.Path]::GetFullPath($Path)
}
function Wait-UserHostReady([scriptblock]$Probe, [int]$TimeoutMilliseconds = 60000, [int]$PollMilliseconds = 500) {
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        if (& $Probe) { return $true }
        if ($elapsed.ElapsedMilliseconds -ge $TimeoutMilliseconds) { return $false }
        Start-Sleep -Milliseconds ([Math]::Min($PollMilliseconds, [Math]::Max(1, $TimeoutMilliseconds - [int]$elapsed.ElapsedMilliseconds)))
    }
}
if ($InstallRoot) { $InstallRoot = Resolve-InstallRoot $InstallRoot }
$expectedVersion = '2.2.4.0'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$targetSid = $identity.User.Value
$targetSession = [Diagnostics.Process]::GetCurrentProcess().SessionId
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or $targetSession -eq 0) {
    throw 'Run this coordinator as the intended, non-elevated signed-in user. Only MSI execution is elevated.'
}

if (-not $UserSetupOnly) {
    if (-not $MsiPath) {
        $packages = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*2.2.4.0*.msi' -File)
        if ($packages.Count -ne 1) { throw 'Specify exactly one Githubie 2.2.4.0 MSI with -MsiPath.' }
        $MsiPath = $packages[0].FullName
    }
    $resolvedMsi = (Resolve-Path -LiteralPath $MsiPath).Path
    if ($resolvedMsi.Contains('"') -or [IO.Path]::GetExtension($resolvedMsi) -ne '.msi') { throw 'Invalid MSI path.' }
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.OpenDatabase($resolvedMsi, 0)
    $view = $database.OpenView('SELECT `Property`, `Value` FROM `Property`')
    $view.Execute()
    $properties = @{}
    while ($record = $view.Fetch()) { $properties[$record.StringData(1)] = $record.StringData(2) }
    $view.Close()
    if ($properties.ProductName -ne 'Githubie' -or $properties.ProductVersion -ne '2.2.4' -or
        $properties.UpgradeCode -ne '{1A5C995A-0F3A-4A4B-B00D-8D9D9BAA6B57}') { throw 'The package is not the expected Githubie user-host MSI.' }
    $ui = if ($Quiet) { '/qn' } else { '/passive' }
    $arguments = "/i `"$resolvedMsi`" $ui /norestart GITHUBIE_USER_SETUP_PENDING=1 GITHUBIE_TARGET_SID=$targetSid"
    if ($InstallRoot) {
        $msiRoot = $InstallRoot.TrimEnd('\')
        if ($msiRoot -match '^[A-Za-z]:$') { $msiRoot += '\\' }
        $arguments += ' INSTALLROOT="' + $msiRoot + '"'
    }
    $process = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -notin @(0, 3010)) { throw "MSI was cancelled or failed (exit $($process.ExitCode)); user setup is incomplete." }
}

# Return to the original coordinator token, even if another administrator approved UAC.
$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
if ($currentIdentity.User.Value -ne $targetSid -or [Diagnostics.Process]::GetCurrentProcess().SessionId -ne $targetSession) {
    throw 'The intended user context changed; startup registration was not attempted.'
}
$machineRegistry = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
try {
    $installationKey = $machineRegistry.OpenSubKey('SOFTWARE\Akatsukisoft\Githubie')
    try { $registeredRoot = if ($installationKey) { $installationKey.GetValue('InstallRoot') } else { $null } }
    finally { if ($installationKey) { $installationKey.Dispose() } }
} finally { $machineRegistry.Dispose() }
if (-not $registeredRoot) { throw 'The installed Githubie location is not registered; user setup is incomplete.' }
$registeredRoot = Resolve-InstallRoot $registeredRoot
if ($InstallRoot -and -not [string]::Equals($InstallRoot.TrimEnd('\'), $registeredRoot.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The installed location does not match InstallRoot; user setup is incomplete.'
}
$binaryDirectory = Join-Path $registeredRoot 'bin'
$cli = Join-Path $binaryDirectory 'githubie.exe'
if (-not (Test-Path -LiteralPath $cli) -or [Diagnostics.FileVersionInfo]::GetVersionInfo($cli).FileVersion -ne $expectedVersion) {
    throw 'The installed Githubie CLI does not match 2.2.4.0; user setup is incomplete.'
}
$setupOutput = & $cli setup-user $targetSid
if ($LASTEXITCODE -ne 0) { throw 'Current-user setup failed; installation is not complete. Legacy data remains available for recovery.' }
$receipt = $setupOutput | ConvertFrom-Json
if ($receipt.owner_sid -ne $targetSid -or $receipt.session_id -ne $targetSession -or $receipt.verified -ne $true) {
    throw 'Startup registration readback failed; user setup is incomplete.'
}
Write-Output "Githubie $expectedVersion installed for $targetSid (session $targetSession), startup=$($receipt.autostart_enabled), verified."
if (-not $Quiet) {
    # Do not capture a launcher command: the long-running host can inherit the
    # PowerShell output pipe and keep capture open after the CLI has returned.
    & $cli host start
    $hostReady = $LASTEXITCODE -eq 0
    if (-not $hostReady) {
        Write-Output 'Initial host startup wait ended; checking the current host for up to 60 more seconds.'
        $hostReady = Wait-UserHostReady -Probe {
            $savedPreference = $ErrorActionPreference
            try {
                $ErrorActionPreference = 'Continue'
                $statusOutput = @(& $cli host status 2>&1)
                $statusExit = $LASTEXITCODE
                return $statusExit -eq 0 -and (($statusOutput -join "`n").Trim() -eq 'ready')
            } finally { $ErrorActionPreference = $savedPreference }
        }
    }
    if (-not $hostReady) { throw 'User setup completed, but host readiness was not confirmed within the startup wait. Check host status and logs before retrying; a timeout does not prove the host has stopped.' }
    Write-Output 'Current user host readiness verified.'
}
