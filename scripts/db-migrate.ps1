# Applies every pending EF Core migration to a database. The game server never does this itself (Persistence section 2).
# Without -ConnectionString it migrates the Compose development database described by docker/.env.
param(
    [string] $ConnectionString
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'DevDatabase.ps1')

if (-not $ConnectionString) {
    $ConnectionString = Get-DevConnectionString $root
}

Push-Location $root
try {
    dotnet tool restore | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'FAILED: dotnet tool restore'
        exit 1
    }

    # The design-time factory reads the connection from the environment, so the password is never an argument.
    $previous = $env:ConnectionStrings__Evertorch
    $env:ConnectionStrings__Evertorch = $ConnectionString
    try {
        dotnet ef database update --project Evertorch.Persistence --configuration Release
    }
    finally {
        $env:ConnectionStrings__Evertorch = $previous
    }
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: dotnet ef database update (exit code $LASTEXITCODE)"
        exit 1
    }
}
finally {
    Pop-Location
}
