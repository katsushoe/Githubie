param(
    [string]$DisplayVersion = '1.8.9.4',
    [string]$ProductVersion = '1.8.9',
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$installerWorkDirectory = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '.local\installer'))
$publishDirectory = Join-Path $repositoryRoot '.local\installer\publish'
$outputDirectory = Join-Path $repositoryRoot '.local\installer\output'
$installerProject = Join-Path $repositoryRoot 'installer\Githubie.Installer\Githubie.Installer.wixproj'
# Publish Githubie.Server last so newer package assemblies it depends on (for example System.Text.Json 10)
# are not overwritten by the runtime copies of the other self-contained projects.
$projects = @(
    'src\Githubie.Cli\Githubie.Cli.csproj',
    'src\Githubie.AskPass\Githubie.AskPass.csproj',
    'src\Githubie.ApprovalPrompt\Githubie.ApprovalPrompt.csproj',
    'src\Githubie.Server\Githubie.Server.csproj'
)

if (-not $NoRestore) {
    foreach ($project in $projects) {
        dotnet restore (Join-Path $repositoryRoot $project) -r $RuntimeIdentifier --nologo
        if ($LASTEXITCODE -ne 0) { throw "Project restore failed: $project" }
    }
    dotnet restore $installerProject --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Installer restore failed.' }
}

foreach ($directory in @($publishDirectory, $outputDirectory)) {
    $resolvedDirectory = [IO.Path]::GetFullPath($directory)
    if (-not $resolvedDirectory.StartsWith("$installerWorkDirectory\", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a directory outside the installer work directory: $resolvedDirectory"
    }
}

if (Test-Path -LiteralPath $publishDirectory) { Remove-Item -LiteralPath $publishDirectory -Recurse -Force }
if (Test-Path -LiteralPath $outputDirectory) { Remove-Item -LiteralPath $outputDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $publishDirectory, $outputDirectory -Force | Out-Null

# Publish each project separately, then merge with a forced overwrite in project order.
# dotnet publish keeps a newer existing file, so a shared output directory can retain an older assembly version.
$stagingDirectory = Join-Path $publishDirectory '..\publish-staging'
$stagingDirectory = [IO.Path]::GetFullPath($stagingDirectory)
if (-not $stagingDirectory.StartsWith("$installerWorkDirectory\", [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a staging directory outside the installer work directory: $stagingDirectory"
}
if (Test-Path -LiteralPath $stagingDirectory) { Remove-Item -LiteralPath $stagingDirectory -Recurse -Force }

foreach ($project in $projects) {
    $projectOutput = Join-Path $stagingDirectory ([IO.Path]::GetFileNameWithoutExtension($project))
    dotnet publish (Join-Path $repositoryRoot $project) -c Release -r $RuntimeIdentifier --self-contained true -o $projectOutput --nologo --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $project" }
    Copy-Item -Path (Join-Path $projectOutput '*') -Destination $publishDirectory -Recurse -Force
}
Remove-Item -LiteralPath $stagingDirectory -Recurse -Force

# Verify that the merged publish directory satisfies the assembly versions required by Githubie.Server.
$serverDeps = Get-Content (Join-Path $publishDirectory 'Githubie.Server.deps.json') -Raw | ConvertFrom-Json
foreach ($target in $serverDeps.targets.PSObject.Properties) {
    foreach ($library in $target.Value.PSObject.Properties) {
        $runtime = $library.Value.runtime
        if ($null -eq $runtime) { continue }
        foreach ($asset in $runtime.PSObject.Properties) {
            if (-not $asset.Value.assemblyVersion) { continue }
            $file = Join-Path $publishDirectory ([IO.Path]::GetFileName($asset.Name))
            if (-not (Test-Path -LiteralPath $file)) { throw "Published assembly is missing: $([IO.Path]::GetFileName($file))" }
            $actual = [Reflection.AssemblyName]::GetAssemblyName($file).Version
            if ($actual -lt [Version]$asset.Value.assemblyVersion) {
                throw "Published assembly is older than Githubie.Server requires: $([IO.Path]::GetFileName($file)) $actual < $($asset.Value.assemblyVersion)"
            }
        }
    }
}

dotnet build $installerProject -c Release --nologo --no-restore -p:DisplayVersion=$DisplayVersion -p:ProductVersion=$ProductVersion -p:PublishDir=$publishDirectory -p:OutputPath=$outputDirectory
if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }

$msiPath = Join-Path $outputDirectory "Githubie-$DisplayVersion-win-x64.msi"
if (-not (Test-Path -LiteralPath $msiPath)) { throw "MSI was not created: $msiPath" }
$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $msiPath
$hashLine = "$($hash.Hash)  $([IO.Path]::GetFileName($msiPath))"
[IO.File]::WriteAllText("$msiPath.sha256", "$hashLine`r`n", [Text.Encoding]::ASCII)
Write-Output $msiPath
Write-Output $hashLine
