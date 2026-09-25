# Starts the game server for local development (run-server.cmd calls this):
#   1. refreshes both content packages, so the Unity client's copy always matches the server's;
#   2. starts the Compose PostgreSQL database and applies pending migrations to it;
#   3. runs the server in the Development environment with that database.
# Extra arguments go to the server, e.g. --Network:Port=7779 --Health:Port=7780.
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $ServerArguments
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'DevDatabase.ps1')

Push-Location $root
try {
    dotnet run --project Evertorch.Tools -- content build --client-out Evertorch.Client/Assets/StreamingAssets/GameData
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'The content build failed; the server was not started.'
        exit 1
    }

    if (-not (Test-DockerEngine)) {
        Write-Host 'Docker is not running. Start Docker Desktop; the server needs its PostgreSQL database.'
        exit 1
    }

    $docker = Get-DockerCommand
    & $docker compose -f docker/compose.yaml up -d --wait
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'The development database did not start (see docker/README.md).'
        exit 1
    }

    $connection = Get-DevConnectionString $root
    & (Join-Path $PSScriptRoot 'db-migrate.ps1') -ConnectionString $connection
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Migrating the development database failed; the server was not started.'
        exit 1
    }

    # Handed over in the environment, not on the command line, so it does not show in the process list.
    $env:ConnectionStrings__Evertorch = $connection
    dotnet run --project Evertorch.Server --launch-profile Development -- @ServerArguments
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'The server stopped with an error.'
        exit 1
    }
}
finally {
    Pop-Location
}
