param([string]$PipeName, [ValidateSet('approval', 'token', 'cancel', 'skip', 'invalid')][string]$Mode)
$ErrorActionPreference = 'Stop'
$pipe = [IO.Pipes.NamedPipeClientStream]::new('.', $PipeName, [IO.Pipes.PipeDirection]::InOut,
    [IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::CurrentUserOnly)
function Read-Exact([byte[]]$Buffer) {
    $offset = 0
    while ($offset -lt $Buffer.Length) {
        $count = $pipe.Read($Buffer, $offset, $Buffer.Length - $offset)
        if ($count -eq 0) { throw 'Unexpected EOF.' }
        $offset += $count
    }
}
try {
    $pipe.Connect(10000)
    $header = [byte[]]::new(4)
    Read-Exact $header
    $length = [BitConverter]::ToInt32($header, 0)
    if ($length -lt 0 -or $length -gt 16384) { throw 'Invalid frame length.' }
    $request = [byte[]]::new($length)
    Read-Exact $request
    $response = switch ($Mode) {
        'approval' { '{"Approved":true}' }
        'token' { '{"Accepted":true,"Token":"fixture-token","Skipped":false}' }
        'cancel' { '{"Accepted":false,"Token":"","Skipped":false}' }
        'skip' { '{"Accepted":false,"Token":"","Skipped":true}' }
        'invalid' { '{' }
    }
    $bytes = [Text.Encoding]::UTF8.GetBytes($response)
    $prefix = [BitConverter]::GetBytes($bytes.Length)
    $pipe.Write($prefix, 0, $prefix.Length)
    $pipe.Write($bytes, 0, $bytes.Length)
    $pipe.Flush()
}
finally { $pipe.Dispose() }
