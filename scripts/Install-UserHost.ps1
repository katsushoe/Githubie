param(
    [string]$MsiPath,
    [switch]$Quiet,
    [switch]$UserSetupOnly
)

$ErrorActionPreference = 'Stop'
$expectedVersion = '2.0.0.0'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$targetSid = $identity.User.Value
$targetSession = [Diagnostics.Process]::GetCurrentProcess().SessionId
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or $targetSession -eq 0) {
    throw 'Run this coordinator as the intended, non-elevated signed-in user. Only MSI execution is elevated.'
}

if (-not $UserSetupOnly) {
    if (-not $MsiPath) {
        $packages = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*2.0.0.0*.msi' -File)
        if ($packages.Count -ne 1) { throw 'Specify exactly one Githubie 2.0.0.0 MSI with -MsiPath.' }
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
    if ($properties.ProductName -ne 'Githubie' -or $properties.ProductVersion -ne '2.0.0' -or
        $properties.UpgradeCode -ne '{1A5C995A-0F3A-4A4B-B00D-8D9D9BAA6B57}') { throw 'The package is not the expected Githubie user-host MSI.' }
    $ui = if ($Quiet) { '/qn' } else { '/passive' }
    $arguments = "/i `"$resolvedMsi`" $ui /norestart GITHUBIE_USER_SETUP_PENDING=1 GITHUBIE_TARGET_SID=$targetSid"
    $process = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -notin @(0, 3010)) { throw "MSI was cancelled or failed (exit $($process.ExitCode)); user setup is incomplete." }
}

# Return to the original coordinator token, even if another administrator approved UAC.
$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
if ($currentIdentity.User.Value -ne $targetSid -or [Diagnostics.Process]::GetCurrentProcess().SessionId -ne $targetSession) {
    throw 'The intended user context changed; startup registration was not attempted.'
}
$binaryDirectory = 'C:\Githubie\bin'
$cli = Join-Path $binaryDirectory 'githubie.exe'
if (-not (Test-Path -LiteralPath $cli) -or [Diagnostics.FileVersionInfo]::GetVersionInfo($cli).FileVersion -ne $expectedVersion) {
    throw 'The installed Githubie CLI does not match 2.0.0.0; user setup is incomplete.'
}
$setupOutput = & $cli setup-user $targetSid
if ($LASTEXITCODE -ne 0) { throw 'Current-user setup failed; installation is not complete. Legacy data remains available for recovery.' }
$receipt = $setupOutput | ConvertFrom-Json
if ($receipt.owner_sid -ne $targetSid -or $receipt.session_id -ne $targetSession -or $receipt.verified -ne $true) {
    throw 'Startup registration readback failed; user setup is incomplete.'
}
Write-Output "Githubie $expectedVersion installed for $targetSid (session $targetSession), startup=$($receipt.autostart_enabled), verified."
if (-not $Quiet) {
    & $cli host start
    if ($LASTEXITCODE -ne 0) { throw 'User setup completed, but the current host could not start. Check host logs and port conflicts.' }
}
