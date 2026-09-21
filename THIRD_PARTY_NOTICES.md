# Third-Party Notices

Evertorch uses the third-party components listed below. Each remains under its
own license.

## NuGet packages

| Package | Version | Used by | License | Project |
| --- | --- | --- | --- | --- |
| LiteNetLib | 2.1.4 | `Evertorch.Server` | MIT | <https://github.com/RevenantX/LiteNetLib> |
| Microsoft.Extensions.Hosting | 10.0.12 | `Evertorch.Server` | MIT | <https://github.com/dotnet/runtime> |
| YamlDotNet | 18.1.0 | `Evertorch.Tools` | MIT | <https://github.com/aaubry/YamlDotNet> |

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
| EmojiOne sample sprites | `Assets/TextMesh Pro/Sprites` | EmojiOne terms | `EmojiOne Attribution.txt` |

LiteNetLib is Copyright (c) Ruslan Pyrch.

Microsoft.Extensions.Hosting is Copyright (c) .NET Foundation and Contributors.

YamlDotNet is Copyright (c) Antoine Aubry and contributors.

TextMesh Pro is Copyright (c) Unity Technologies ApS.

Liberation Sans is Copyright (c) 2012 Red Hat, Inc., with digitized data
Copyright (c) 2010 Google Corporation.

The EmojiOne sample sprites are provided by EmojiOne (<https://www.emojione.com/>).
