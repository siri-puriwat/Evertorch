# Local infrastructure

This directory contains local development infrastructure only. It is not a
deployment configuration.

## Runtime prerequisites

- The .NET 10 SDK (see `global.json`) builds and runs everything here.
- A machine that only runs a built server needs both the .NET Runtime 10 and
  the ASP.NET Core Runtime 10: the server's health endpoints use the ASP.NET
  Core shared framework.
- Docker Desktop, running, for the database below and for the database tests.

## PostgreSQL 18

1. Copy `.env.example` to `.env` and set `EVERTORCH_POSTGRES_PASSWORD`. The
   `.env` file is ignored by Git; Compose refuses to start without a password.
2. Start the database: `docker compose -f docker/compose.yaml up -d --wait`
3. Stop it: `docker compose -f docker/compose.yaml down`
4. Discard all local data: `docker compose -f docker/compose.yaml down -v`

The database listens on `127.0.0.1` only.

## Schema

The schema is created by EF Core migrations in `Evertorch.Persistence/Migrations`.
The game server never applies them: it refuses to start while one is pending.

- Apply pending migrations to this database: `scripts/db-migrate.ps1` (it
  builds the connection from `.env`; pass `-ConnectionString` for another
  database).
- `scripts/run-server.cmd` starts this database, migrates it, and gives the
  server its connection string, so local play needs nothing else.
- Database tests start their own throwaway `postgres:18` containers with
  Testcontainers and never touch this database. `scripts/verify.ps1` therefore
  needs Docker running.

## Application configuration

Runtime configuration is external to compiled code: JSON files for non-secret
defaults, overridden by environment variables. Secrets are supplied through
environment variables or .NET user secrets and are never committed.

| Setting | JSON key | Environment variable |
| --- | --- | --- |
| Database connection | `ConnectionStrings:Evertorch` | `ConnectionStrings__Evertorch` |

The server refuses to start without it. The IDE launch profiles do not set it:
set the environment variable first (see "Running the server from Visual Studio"
below), or use `scripts/run-server.cmd`. Example for a local shell, using the
values chosen in `.env`:

```text
ConnectionStrings__Evertorch=Host=127.0.0.1;Port=5432;Database=evertorch_dev;Username=evertorch;Password=<local password>
```

## Server ports, health checks, and stopping

- Game traffic: UDP 7777, on `127.0.0.1` unless the server is started with
  `--Network:BindAddress=0.0.0.0` (the `Development (LAN)` launch profile does
  this). Another device on the network also needs an inbound firewall rule for
  UDP 7777.
- Health checks: HTTP on TCP 7778, on `127.0.0.1` only.
  `http://127.0.0.1:7778/health/live` answers 200 while the simulation runs;
  `http://127.0.0.1:7778/health/ready` answers 200 while the server also
  admits players and its database answers. Otherwise they answer 503; both
  return a JSON body. In Windows PowerShell use `curl.exe` or
  `Invoke-RestMethod`, since `curl` is an alias there.
- A second server on the same machine needs other ports, for example
  `--Network:Port=7779 --Health:Port=7780`; otherwise it exits with code 1.
- Ctrl+C in the server's window, or `shutdown` typed into it, stops it cleanly:
  connected players are saved and told, and the process exits with code 0.
  Closing the window instead kills the process before that.

## Running the server from Visual Studio

The launch profiles in `Evertorch.Server/Properties/launchSettings.json` are
committed, so they cannot carry the database password. Visual Studio takes the
connection string from a user environment variable instead.

One-time setup, from the repository root, with Docker Desktop running:

1. Run `scripts/run-server.cmd` once, then stop it with Ctrl+C. It builds the
   content packages, starts the database, and applies the migrations. The
   database keeps running.
2. Store the connection string. It is built from `.env`, so the password is
   neither typed nor printed:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -Command ". .\scripts\DevDatabase.ps1; [Environment]::SetEnvironmentVariable('ConnectionStrings__Evertorch', (Get-DevConnectionString (Get-Location).Path), 'User')"
   ```

   It prints nothing when it works.
3. Check it, with the password masked:

   ```powershell
   [Environment]::GetEnvironmentVariable('ConnectionStrings__Evertorch', 'User') -replace 'Password=[^;]*', 'Password=***'
   ```

4. Close every Visual Studio window and open it again. It reads environment
   variables only when it starts.
5. Choose the `Development` or `Development (LAN)` profile and start debugging.

Afterwards:

- Docker Desktop must be running. The database container restarts with it.
- After pulling a new migration, run `scripts/db-migrate.ps1` first: the server
  exits while a migration is pending.
- After changing files under `content/`, rebuild the packages with
  `dotnet run --project Evertorch.Tools -- content build --client-out Evertorch.Client/Assets/StreamingAssets/GameData`,
  or run `scripts/run-server.cmd` again.
- After changing the password in `.env`, repeat step 2.
- To remove the variable:
  `[Environment]::SetEnvironmentVariable('ConnectionStrings__Evertorch', $null, 'User')`.

The variable holds only the local development password, for a database that
listens on `127.0.0.1`.

To debug without the variable, start `scripts/run-server.cmd` and use
Debug > Attach to Process on the `Evertorch.Server` process.
