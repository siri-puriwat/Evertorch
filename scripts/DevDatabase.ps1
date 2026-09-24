# Shared by the local scripts that need the development database. Dot-source it; it defines functions only.

# The Docker CLI: from PATH, or where Docker Desktop installs it (some shells start without it on PATH).
function Get-DockerCommand {
    $command = Get-Command docker -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $installed = Join-Path $env:ProgramFiles 'Docker\Docker\resources\bin\docker.exe'
    if (Test-Path $installed) {
        return $installed
    }

    throw 'The Docker CLI was not found. Install Docker Desktop, or put docker on PATH.'
}

# True when the Docker engine answers, not merely when the CLI is installed.
function Test-DockerEngine {
    try {
        $docker = Get-DockerCommand
    }
    catch {
        return $false
    }

    # Windows PowerShell turns a native command's redirected stderr into an error record, which the callers' 'Stop'
    # preference makes fatal; a stopped engine must answer false so the caller can say what to do.
    $ErrorActionPreference = 'Continue'
    & $docker info --format '{{.ServerVersion}}' *> $null
    return $LASTEXITCODE -eq 0
}

# The connection string of the Compose database (docker/compose.yaml), built from the ignored docker/.env.
function Get-DevConnectionString([string] $root) {
    $envFile = Join-Path $root 'docker/.env'
    if (-not (Test-Path $envFile)) {
        throw 'docker/.env is missing. Copy docker/.env.example to docker/.env and set EVERTORCH_POSTGRES_PASSWORD.'
    }

    $values = @{
        EVERTORCH_POSTGRES_DB = 'evertorch_dev'
        EVERTORCH_POSTGRES_USER = 'evertorch'
        EVERTORCH_POSTGRES_PORT = '5432'
    }
    foreach ($line in Get-Content -LiteralPath $envFile) {
        if ($line -match '^\s*([A-Z_]+)\s*=\s*(.*?)\s*$') {
            $values[$Matches[1]] = $Matches[2]
        }
    }

    if (-not $values['EVERTORCH_POSTGRES_PASSWORD']) {
        throw 'EVERTORCH_POSTGRES_PASSWORD is empty in docker/.env.'
    }

    return "Host=127.0.0.1;Port=$($values['EVERTORCH_POSTGRES_PORT']);Database=$($values['EVERTORCH_POSTGRES_DB']);" +
        "Username=$($values['EVERTORCH_POSTGRES_USER']);Password=$($values['EVERTORCH_POSTGRES_PASSWORD'])"
}
