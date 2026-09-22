using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Evertorch.Tools
{
/// <summary>
///     Turns validated content into the server and client packages, entirely in memory.
/// </summary>
public static class ContentPackageBuilder
{
    public const int SchemaVersion = 1;

    public static ContentPackages Build(ContentSet content)
    {
        IReadOnlyList<PackageFile> clientFiles = ClientProjection.Write(content);
        string clientVersion = ComputeVersion(clientFiles);

        IReadOnlyList<PackageFile> serverFiles = ServerProjection.Write(content);
        string serverVersion = ComputeVersion(serverFiles);

        // Only the server learns both versions; the client manifest stays silent about server content.
        PackageFile clientManifest = WriteManifest(clientFiles, null, clientVersion);
        PackageFile serverManifest = WriteManifest(serverFiles, serverVersion, clientVersion);

        return new ContentPackages(
            new ContentPackage(serverVersion, serverFiles, serverManifest),
            new ContentPackage(clientVersion, clientFiles, clientManifest));
    }

    public static string ComputeHash(byte[] content)
    {
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    // A version changes exactly when some file's path or bytes change, so the two sides advance independently.
    private static string ComputeVersion(IReadOnlyList<PackageFile> files)
    {
        var listing = new StringBuilder();
        foreach (PackageFile file in files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            listing.Append(file.Path).Append(':').Append(ComputeHash(file.Content)).Append('\n');
        }

        return ComputeHash(Encoding.UTF8.GetBytes(listing.ToString())).Substring(0, 16);
    }

    private static PackageFile WriteManifest(
        IReadOnlyList<PackageFile> files,
        string? serverVersion,
        string clientVersion)
    {
        byte[] content = PackageJson.Write(writer => WriteManifestBody(writer, files, serverVersion, clientVersion));
        return new PackageFile(ContentPackage.ManifestPath, content);
    }

    private static void WriteManifestBody(
        Utf8JsonWriter writer,
        IReadOnlyList<PackageFile> files,
        string? serverVersion,
        string clientVersion)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", SchemaVersion);
        if (serverVersion != null)
        {
            writer.WriteString("serverContentVersion", serverVersion);
        }

        writer.WriteString("clientContentVersion", clientVersion);
        writer.WriteStartArray("files");
        foreach (PackageFile file in files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("path", file.Path);
            writer.WriteString("sha256", ComputeHash(file.Content));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
}
