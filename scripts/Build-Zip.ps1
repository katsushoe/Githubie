param(
    [string]$DisplayVersion = '1.8.9.3',
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseWorkDirectory = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '.local\release'))
$stagingDirectory = Join-Path $releaseWorkDirectory 'staging'
$outputDirectory = Join-Path $releaseWorkDirectory 'output'

if (-not $NoRestore) {
    dotnet restore (Join-Path $repositoryRoot 'src\Githubie.Cli\Githubie.Cli.csproj') -r $RuntimeIdentifier --nologo
    if ($LASTEXITCODE -ne 0) { throw 'CLI restore failed.' }
    dotnet restore (Join-Path $repositoryRoot 'src\Githubie.Server\Githubie.Server.csproj') -r $RuntimeIdentifier --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Server restore failed.' }
    dotnet restore (Join-Path $repositoryRoot 'src\Githubie.AskPass\Githubie.AskPass.csproj') -r $RuntimeIdentifier --nologo
    if ($LASTEXITCODE -ne 0) { throw 'AskPass restore failed.' }
    dotnet restore (Join-Path $repositoryRoot 'src\Githubie.ApprovalPrompt\Githubie.ApprovalPrompt.csproj') -r $RuntimeIdentifier --nologo
    if ($LASTEXITCODE -ne 0) { throw 'ApprovalPrompt restore failed.' }
}

foreach ($directory in @($stagingDirectory, $outputDirectory)) {
    $resolvedDirectory = [IO.Path]::GetFullPath($directory)
    if (-not $resolvedDirectory.StartsWith("$releaseWorkDirectory\", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a directory outside the release work directory: $resolvedDirectory"
    }
}

if (Test-Path -LiteralPath $stagingDirectory) { Remove-Item -LiteralPath $stagingDirectory -Recurse -Force }
if (Test-Path -LiteralPath $outputDirectory) { Remove-Item -LiteralPath $outputDirectory -Recurse -Force }

$binDirectory = Join-Path $stagingDirectory 'bin'
$configDirectory = Join-Path $stagingDirectory 'config'
$logDirectory = Join-Path $stagingDirectory 'logs'
$dataDirectory = Join-Path $stagingDirectory 'data'
$secretDirectory = Join-Path $dataDirectory 'secrets'
$docsDirectory = Join-Path $stagingDirectory 'docs'
New-Item -ItemType Directory -Path $binDirectory, $configDirectory, $logDirectory, $dataDirectory, $secretDirectory, $docsDirectory, $outputDirectory -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $logDirectory '.keep'), '', [Text.Encoding]::ASCII)
[IO.File]::WriteAllText((Join-Path $secretDirectory '.keep'), '', [Text.Encoding]::ASCII)

# Publish each project separately, then merge with a forced overwrite and Githubie.Server last.
# dotnet publish keeps a newer existing file, so a shared output directory can retain an older assembly version.
$publishStagingDirectory = Join-Path $releaseWorkDirectory 'publish-staging'
if (Test-Path -LiteralPath $publishStagingDirectory) { Remove-Item -LiteralPath $publishStagingDirectory -Recurse -Force }
foreach ($project in @(
        'src\Githubie.Cli\Githubie.Cli.csproj',
        'src\Githubie.AskPass\Githubie.AskPass.csproj',
        'src\Githubie.ApprovalPrompt\Githubie.ApprovalPrompt.csproj',
        'src\Githubie.Server\Githubie.Server.csproj')) {
    $projectOutput = Join-Path $publishStagingDirectory ([IO.Path]::GetFileNameWithoutExtension($project))
    dotnet publish (Join-Path $repositoryRoot $project) -c Release -r $RuntimeIdentifier --self-contained true -o $projectOutput --nologo --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $project" }
    Copy-Item -Path (Join-Path $projectOutput '*') -Destination $binDirectory -Recurse -Force
}
Remove-Item -LiteralPath $publishStagingDirectory -Recurse -Force

# Verify that the merged bin directory satisfies the assembly versions required by Githubie.Server.
$serverDeps = Get-Content (Join-Path $binDirectory 'Githubie.Server.deps.json') -Raw | ConvertFrom-Json
foreach ($target in $serverDeps.targets.PSObject.Properties) {
    foreach ($library in $target.Value.PSObject.Properties) {
        $runtime = $library.Value.runtime
        if ($null -eq $runtime) { continue }
        foreach ($asset in $runtime.PSObject.Properties) {
            if (-not $asset.Value.assemblyVersion) { continue }
            $file = Join-Path $binDirectory ([IO.Path]::GetFileName($asset.Name))
            if (-not (Test-Path -LiteralPath $file)) { throw "Published assembly is missing: $([IO.Path]::GetFileName($file))" }
            $actual = [Reflection.AssemblyName]::GetAssemblyName($file).Version
            if ($actual -lt [Version]$asset.Value.assemblyVersion) {
                throw "Published assembly is older than Githubie.Server requires: $([IO.Path]::GetFileName($file)) $actual < $($asset.Value.assemblyVersion)"
            }
        }
    }
}

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'githubie.example.json') -Destination $configDirectory
  $documents = @(
      'README.md',
      'README.ja.md',
      'DOCUMENTS.md',
      'DOCUMENTS.ja.md',
      'MCP_SETUP.md',
      'MCP_SETUP.ja.md',
      'INSTALLATION.md',
      'INSTALLATION.ja.md',
      'OPERATIONS.md',
      'OPERATIONS.ja.md',
      'TROUBLESHOOTING.md',
      'TROUBLESHOOTING.ja.md',
      'CONFIG.md',
      'CONFIG.ja.md',
      'COMMANDS.md',
      'COMMANDS.ja.md',
      'SECURITY.md',
      'SECURITY.ja.md',
      'PACKAGES.md',
      'PACKAGES.ja.md',
      'RELEASE.md',
      'RELEASE.ja.md',
      'LICENSE'
)
foreach ($document in $documents) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $document) -Destination $docsDirectory
}

$zipPath = Join-Path $outputDirectory "Githubie-$DisplayVersion-win-x64.zip"
Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath
$hashLine = "$($hash.Hash)  $([IO.Path]::GetFileName($zipPath))"
[IO.File]::WriteAllText("$zipPath.sha256", "$hashLine`r`n", [Text.Encoding]::ASCII)

Write-Output $zipPath
Write-Output $hashLine
