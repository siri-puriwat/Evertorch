# Serves the web build in artifacts/client/Web over HTTP and opens it in the default browser (serve-web.cmd calls
# this); Ctrl+C stops it. It takes the first free port from 8000, or the one -Port names.
param([int] $Port)

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

# Windows reserves ranges of ports for Hyper-V, WSL, and Docker (netsh interface ipv4 show excludedportrange
# protocol=tcp), and they move between restarts; a bind there fails with an access error, as does a port in use.
function Test-PortFree([int] $candidate) {
    foreach ($address in [Net.IPAddress]::Any, [Net.IPAddress]::IPv6Any) {
        $listener = New-Object Net.Sockets.TcpListener $address, $candidate
        try {
            $listener.Start()
        }
        catch [Net.Sockets.SocketException] {
            return $false
        }
        finally {
            $listener.Stop()
        }
    }

    return $true
}

if ($Port) {
    if (-not (Test-PortFree $Port)) {
        Write-Host "Port $Port is in use or reserved by Windows; leave out -Port to take the first free one."
        exit 1
    }
}
else {
    $Port = 8000..8999 | Where-Object { Test-PortFree $_ } | Select-Object -First 1
    if (-not $Port) {
        Write-Host 'No free port between 8000 and 8999; name one with -Port.'
        exit 1
    }
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
    Write-Host "The web server did not start on port $Port."
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
