param([string]$BuildDirectory = (Join-Path $PSScriptRoot '../src/Githubie.Server/bin/Debug/net9.0'))

$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('githubie-http-' + [Guid]::NewGuid().ToString('N'))
$testBin = Join-Path $testRoot 'bin'
[IO.Directory]::CreateDirectory($testBin) | Out-Null
Copy-Item -Path (Join-Path $BuildDirectory '*') -Destination $testBin -Recurse
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$endpoint = "http://127.0.0.1:$port/mcp"
$projectId = '11111111-1111-1111-1111-111111111111'
$trustPath = Join-Path $testRoot 'trust.json'
$configPath = Join-Path $testRoot 'test.json'
$signer = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
$issuedTokens = [Collections.Generic.List[string]]::new()
$results = [Collections.Generic.List[object]]::new()
$serverProcess = $null

function Encode-Base64Url([byte[]]$Bytes) {
    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}
function New-TestAssertion([string]$Audience = 'githubie', [int]$AgeSeconds = 0) {
    $issued = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() - $AgeSeconds
    $header = @{ typ = 'JWT'; alg = 'ES256'; kid = 'isolated-test' } | ConvertTo-Json -Compress
    $claims = @{ iss = 'moyai:isolated'; sub = 'moyai:isolated'; aud = $Audience; prv = $Audience
        iat = $issued; nbf = $issued; exp = $issued + 120; jti = [Guid]::NewGuid().ToString('N')
        project = $projectId; repository = 'github.com/example/isolated'; scope = @('repository.read')
        protocol_version = '1'; operation_id = 'isolated-operation' } | ConvertTo-Json -Compress
    $unsigned = (Encode-Base64Url ([Text.Encoding]::UTF8.GetBytes($header))) + '.' + (Encode-Base64Url ([Text.Encoding]::UTF8.GetBytes($claims)))
    $signature = $signer.SignData([Text.Encoding]::ASCII.GetBytes($unsigned), [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
    $token = $unsigned + '.' + (Encode-Base64Url $signature)
    $issuedTokens.Add($token)
    return $token
}
function Invoke-TestRpc([string]$Method, [hashtable]$Parameters, [string]$Assertion = '', [switch]$Direct) {
    $headers = @{ Accept = 'application/json, text/event-stream'; 'MCP-Protocol-Version' = '2025-03-26' }
    if (-not $Direct) { $headers['X-Moyai-Operation-Id'] = 'isolated-operation' }
    if ($Assertion) { $headers.Authorization = 'Bearer ' + $Assertion }
    $body = @{ jsonrpc = '2.0'; id = 1; method = $Method; params = $Parameters } | ConvertTo-Json -Depth 12 -Compress
    return Invoke-WebRequest -Uri $endpoint -Method Post -Headers $headers -ContentType 'application/json' -Body $body -SkipHttpErrorCheck -TimeoutSec 10
}
function Assert-Http([string]$Name, $Response, [int]$ExpectedStatus) {
    if ([int]$Response.StatusCode -ne $ExpectedStatus) { throw "$Name returned HTTP $($Response.StatusCode), expected $ExpectedStatus" }
    $results.Add([pscustomobject]@{ name = $Name; status = 'passed'; http = $ExpectedStatus })
}
function Start-TestServer {
    $script:serverProcess = Start-Process -FilePath (Join-Path $testBin 'Githubie.Server.exe') -ArgumentList ('"' + $configPath + '"') -WindowStyle Hidden -PassThru
    $statePath = Join-Path $testRoot 'data/service-state.json'
    for ($attempt = 0; $attempt -lt 150; $attempt++) {
        if ($script:serverProcess.HasExited) { throw 'Isolated server exited before readiness.' }
        if (Test-Path -LiteralPath $statePath) {
            try { $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } catch { $state = $null }
            if ($state.status -eq 'ready' -and $state.process_id -eq $script:serverProcess.Id) { return }
        }
        Start-Sleep -Milliseconds 200
    }
    throw 'Isolated server readiness timed out.'
}
try {
    $key = $signer.ExportParameters($false)
    $trust = @{ issuer = 'moyai:isolated'; kid = 'isolated-test'; algorithm = 'ES256'; x = (Encode-Base64Url $key.Q.X); y = (Encode-Base64Url $key.Q.Y)
        not_before_utc = [DateTimeOffset]::UtcNow.AddMinutes(-10).ToString('o'); not_after_utc = [DateTimeOffset]::UtcNow.AddHours(1).ToString('o'); status = 'active' }
    ConvertTo-Json -InputObject @($trust) -Depth 5 | Set-Content -LiteralPath $trustPath -Encoding utf8
    $config = @{ mcp_port = $port; mcp_path = '/mcp'; provider_authentication = @{ issuer = 'moyai:isolated'; trust_bundle_path = $trustPath
        replay_database_path = (Join-Path $testRoot 'replay.db'); clock_skew_seconds = 0; projects = @{ isolated = $projectId } }
        repositories = @{ isolated = @{ github_owner = 'example'; github_repo = 'isolated'; local_root = (Join-Path $testRoot 'absent-repository')
            remote = 'origin'; develop_branch = 'develop'; main_branch = 'main'; direct_push_branches = @('develop'); pull_branches = @('develop')
            protected_branches = @('main'); tag_target_branch = 'main'; tag_pattern = '^v'; merge_method = 'merge'; require_clean_working_tree = $true } } }
    $config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath -Encoding utf8
    Start-TestServer
    Assert-Http 'initialize' (Invoke-TestRpc 'initialize' @{ protocolVersion = '2025-03-26'; capabilities = @{}; clientInfo = @{ name = 'isolated-http-test'; version = '1' } }) 200
    Assert-Http 'discovery' (Invoke-TestRpc 'tools/list' @{}) 200
    $parameters = @{ name = 'github_repository_status'; arguments = @{ repository = 'isolated' } }
    Assert-Http 'missing assertion' (Invoke-TestRpc 'tools/call' $parameters) 401
    Assert-Http 'direct local call without Moyai headers' (Invoke-TestRpc 'tools/call' $parameters -Direct) 200
    Assert-Http 'legacy token' (Invoke-TestRpc 'tools/call' $parameters 'test-legacy-token') 401
    Assert-Http 'wrong audience' (Invoke-TestRpc 'tools/call' $parameters (New-TestAssertion 'buckettie')) 403
    Assert-Http 'expired assertion' (Invoke-TestRpc 'tools/call' $parameters (New-TestAssertion 'githubie' 200)) 401
    $accepted = New-TestAssertion
    $response = Invoke-TestRpc 'tools/call' $parameters $accepted
    Assert-Http 'valid assertion reaches MCP' $response 200
    if ($response.Content -notmatch 'structuredContent') { throw 'MCP did not return a structured Gateway result.' }
    Assert-Http 'replay' (Invoke-TestRpc 'tools/call' $parameters $accepted) 401
    $serverProcess.Kill($true)
    $serverProcess.WaitForExit()
    Start-TestServer
    Assert-Http 'replay after restart' (Invoke-TestRpc 'tools/call' $parameters $accepted) 401
    $trust.status = 'revoked'
    ConvertTo-Json -InputObject @($trust) -Depth 5 | Set-Content -LiteralPath $trustPath -Encoding utf8
    Assert-Http 'revoked trust' (Invoke-TestRpc 'tools/call' $parameters (New-TestAssertion)) 401
    $logs = (Get-ChildItem -LiteralPath (Join-Path $testRoot 'logs') -File | Get-Content -Raw) -join "`n"
    foreach ($token in $issuedTokens) { if ($logs.Contains($token)) { throw 'Assertion appeared in logs.' } }
    $results.Add([pscustomobject]@{ name = 'assertions absent from logs'; status = 'passed' })
    $results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testRoot 'result.json') -Encoding utf8
    Write-Output "Passed $($results.Count) HTTP assertions. Results: $testRoot"
} finally {
    if ($serverProcess -and !$serverProcess.HasExited) { $serverProcess.Kill($true); $serverProcess.WaitForExit() }
    $signer.Dispose()
}
