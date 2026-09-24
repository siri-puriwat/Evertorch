# Third-Party Notices

Evertorch uses the third-party components listed below. Each remains under its
own license.

## NuGet packages

| Package | Version | Used by | License | Project |
| --- | --- | --- | --- | --- |
| LiteNetLib | 2.1.4 | `Evertorch.Server` | MIT | <https://github.com/RevenantX/LiteNetLib> |
| Microsoft.EntityFrameworkCore | 10.0.12 | `Evertorch.Persistence` | MIT | <https://github.com/dotnet/efcore> |
| Microsoft.EntityFrameworkCore.Relational | 10.0.12 | `Evertorch.Persistence` | MIT | <https://github.com/dotnet/efcore> |
| Microsoft.EntityFrameworkCore.Design | 10.0.12 | `Evertorch.Persistence` (migrations tooling only, not shipped) | MIT | <https://github.com/dotnet/efcore> |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 | `Evertorch.Persistence` | PostgreSQL License | <https://github.com/npgsql/efcore.pg> |
| YamlDotNet | 18.1.0 | `Evertorch.Tools` | MIT | <https://github.com/aaubry/YamlDotNet> |

The server also uses the ASP.NET Core shared framework, which the .NET
installation provides; it is not copied into this repository or the build output.

| Framework | Version | Used by | License | Project |
| --- | --- | --- | --- | --- |
| Microsoft.AspNetCore.App (Kestrel for the health endpoints, and the generic host) | 10.0 | `Evertorch.Server` | MIT | <https://github.com/dotnet/aspnetcore> |

Test and development tools, not shipped with the server or the client:

| Package | Version | Used by | License | Project |
| --- | --- | --- | --- | --- |
| Testcontainers.PostgreSql | 4.15.0 | `Evertorch.Persistence.Tests`, `Evertorch.Server.Tests` | MIT | <https://github.com/testcontainers/testcontainers-dotnet> |
| dotnet-ef (local tool) | 10.0.12 | `scripts/db-migrate.ps1` | MIT | <https://github.com/dotnet/efcore> |

## Unity packages

| Package | Version | Used by | License | Project |
| --- | --- | --- | --- | --- |
| com.revenantx.litenetlib | 2.1.4 (git tag `2.1.4-upm`) | `Evertorch.Client` | MIT | <https://github.com/RevenantX/LiteNetLib> |

The Unity package is fetched as source by the Unity Package Manager; no copy is
stored in this repository.

## Unity assets

TextMesh Pro Essential Resources are imported from `com.unity.ugui` 2.0.0 into
`Evertorch.Client/Assets/TextMesh Pro` and stored in this repository.

| Asset | Location | License | Notice |
| --- | --- | --- | --- |
| TextMesh Pro shaders, settings, and font assets | `Assets/TextMesh Pro` | Unity Companion License | <https://unity3d.com/legal/licenses/unity_companion_license> |
| Liberation Sans font | `Assets/TextMesh Pro/Fonts` | SIL Open Font License 1.1 | `LiberationSans - OFL.txt` |

LiteNetLib is Copyright (c) Ruslan Pyrch.

ASP.NET Core, Entity Framework Core, and dotnet-ef are Copyright (c) .NET Foundation and Contributors.

Npgsql.EntityFrameworkCore.PostgreSQL is Copyright 2025 (c) The Npgsql Development Team.

Testcontainers for .NET is Copyright (c) 2019 - 2026 Andre Hofmeister and other authors.

YamlDotNet is Copyright (c) Antoine Aubry and contributors.

TextMesh Pro is Copyright (c) Unity Technologies ApS.

Liberation Sans is Copyright (c) 2012 Red Hat, Inc., with digitized data
Copyright (c) 2010 Google Corporation.
