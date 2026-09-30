# Serves the web build in artifacts/client/Web over HTTP and opens it in the default browser (serve-web.cmd calls
# this); Ctrl+C stops it. -Port changes the port from 8000.
param([int] $Port = 8000)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'artifacts/client/Web'

if (-not (Test-Path (Join-Path $output 'index.html'))) {
    Write-Host 'No web build yet: run scripts\build-web.cmd.'
    exit 1
}

$python = Get-Command py, python -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $python) {
    Write-Host 'Python 3 was not found; it serves the build. Install it, or serve artifacts\client\Web another way.'
    exit 1
}

$pythonArguments = @('-m', 'http.server', $Port, '--directory', "`"$output`"")
if ($python.Name -eq 'py.exe') {
    $pythonArguments = @('-3') + $pythonArguments
}

$server = Start-Process -FilePath $python.Source -ArgumentList $pythonArguments -NoNewWindow -PassThru
$url = "http://localhost:$Port"
$deadline = (Get-Date).AddSeconds(10)
$listening = $false
while (-not $listening -and -not $server.HasExited -and (Get-Date) -lt $deadline) {
    $client = New-Object Net.Sockets.TcpClient
    try {
        $client.Connect('127.0.0.1', $Port)
        $listening = $true
    }
    catch {
        Start-Sleep -Milliseconds 200
    }
    finally {
        $client.Dispose()
    }
}

if (-not $listening) {
    Write-Host "The web server did not start on port $Port; is another program using it? Try -Port 8001."
    if (-not $server.HasExited) {
        Stop-Process -Id $server.Id
    }

    exit 1
}

Write-Host "Serving $output at $url. Press Ctrl+C to stop."
Start-Process $url
try {
    $server.WaitForExit()
}
finally {
    if (-not $server.HasExited) {
        Stop-Process -Id $server.Id
    }
}
