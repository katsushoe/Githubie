$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$data = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($env:GITHUBIE_UPDATE_INPUT)) | ConvertFrom-Json
Remove-Item Env:GITHUBIE_UPDATE_INPUT
$packageLock = $null
$ownerExited = $false
$outcome = 'UpdateFailed'
$folder = [IO.Path]::GetDirectoryName($data.path)

function Quote-Argument([string]$value) {
    '"' + (($value -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"'
}

try {
    $owner = Get-Process -Id ([int]$data.owner)
    if ($owner.StartTime.ToUniversalTime().ToFileTimeUtc().ToString([Globalization.CultureInfo]::InvariantCulture) -cne $data.ownerStart) {
        throw 'The startup update owner changed.'
    }

    # Keep the inspected package readable but prevent writes and replacement through installation.
    $packageLock = [IO.File]::Open($data.path, 'Open', 'Read', 'Read')
    $signature = Get-AuthenticodeSignature -LiteralPath $data.path
    if ($signature.Status -ne 'Valid' -or
        $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -cne 'Akatsukisoft') {
        throw 'The package is not signed by the Githubie publisher.'
    }
    $machineTrust = [Security.Cryptography.X509Certificates.X509Chain]::new($true)
    try {
        $machineTrust.ChainPolicy.UrlRetrievalTimeout = [TimeSpan]::FromSeconds(15)
        [void]$machineTrust.ChainPolicy.ApplicationPolicy.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
        if (-not $machineTrust.Build($signature.SignerCertificate)) {
            throw 'The publisher certificate is not trusted by the machine for code signing.'
        }
    } finally { $machineTrust.Dispose() }

    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.OpenDatabase($data.path, 0)
    $view = $database.OpenView('SELECT `Property`, `Value` FROM `Property`')
    $view.Execute()
    $properties = @{}
    while ($record = $view.Fetch()) {
        $properties[$record.StringData(1)] = $record.StringData(2)
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
    }
    $view.Close()
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
    if ($properties.ProductName -cne 'Githubie' -or $properties.Manufacturer -cne 'Akatsukisoft' -or
        $properties.ProductVersion -cne $data.version -or
        $properties.UpgradeCode -ne '{1A5C995A-0F3A-4A4B-B00D-8D9D9BAA6B57}') {
        throw 'The signed package does not match the expected Githubie version.'
    }

    [IO.File]::WriteAllText($data.ready, '')
    if (-not $owner.WaitForExit(45000) -or -not [IO.File]::Exists($data.commit)) { return }
    $ownerExited = $true

    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $systemDirectory = [Environment]::SystemDirectory
    $arguments = '/i {0} /passive /norestart GITHUBIE_USER_SETUP_PENDING=1 GITHUBIE_TARGET_SID={1} INSTALLROOT={2}' -f
        (Quote-Argument $data.path), $sid, (Quote-Argument $data.installRoot)
    $process = Start-Process -FilePath (Join-Path $systemDirectory 'msiexec.exe') -WorkingDirectory $systemDirectory -ArgumentList $arguments -Verb RunAs -PassThru
    $process.WaitForExit()
    switch ($process.ExitCode) {
        0 { $outcome = 'UpdateInstalled' }
        3010 { $outcome = 'UpdateRebootRequired' }
        1602 { $outcome = 'UpdateCancelled' }
        default { $outcome = 'UpdateFailed' }
    }
}
catch {
    $errorDetail = $_.Exception
    while ($null -ne $errorDetail) {
        if ($errorDetail -is [ComponentModel.Win32Exception] -and $errorDetail.NativeErrorCode -eq 1223) {
            $outcome = 'UpdateCancelled'
            break
        }
        $errorDetail = $errorDetail.InnerException
    }
}
finally {
    if ($null -ne $packageLock) { $packageLock.Dispose() }
    if ($ownerExited) {
        $env:GITHUBIE_SKIP_STARTUP_UPDATE = $outcome
        if ([IO.File]::Exists($data.executable)) {
            $launch = New-Object Diagnostics.ProcessStartInfo
            $launch.FileName = $data.executable
            $launch.Arguments = ($data.hostArguments | ForEach-Object { Quote-Argument $_ }) -join ' '
            $launch.UseShellExecute = $false
            $launch.CreateNoWindow = $true
            try { [Diagnostics.Process]::Start($launch).Dispose() } catch { }
        }
    }
    Remove-Item -LiteralPath $data.path, $data.ready, $data.commit -Force -ErrorAction SilentlyContinue
    try { [IO.Directory]::Delete($folder) } catch { }
}
