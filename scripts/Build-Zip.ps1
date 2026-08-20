param(
    [string]$DisplayVersion = '1.3.5.3',
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

dotnet publish (Join-Path $repositoryRoot 'src\Githubie.Cli\Githubie.Cli.csproj') -c Release -r $RuntimeIdentifier --self-contained true -o $binDirectory --nologo --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed: Githubie.Cli.' }

dotnet publish (Join-Path $repositoryRoot 'src\Githubie.Server\Githubie.Server.csproj') -c Release -r $RuntimeIdentifier --self-contained true -o $binDirectory --nologo --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed: Githubie.Server.' }

dotnet publish (Join-Path $repositoryRoot 'src\Githubie.AskPass\Githubie.AskPass.csproj') -c Release -r $RuntimeIdentifier --self-contained true -o $binDirectory --nologo --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed: Githubie.AskPass.' }

dotnet publish (Join-Path $repositoryRoot 'src\Githubie.ApprovalPrompt\Githubie.ApprovalPrompt.csproj') -c Release -r $RuntimeIdentifier --self-contained true -o $binDirectory --nologo --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed: Githubie.ApprovalPrompt.' }

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
