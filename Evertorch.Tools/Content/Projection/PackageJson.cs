using System;
using System.IO;
using System.Text.Json;

namespace Evertorch.Tools
{
internal static class PackageJson
{
    // Line endings and indentation are pinned so identical content yields identical bytes on every platform.
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        IndentSize = 2,
        NewLine = "\n"
    };

    public static byte[] Write(Action<Utf8JsonWriter> writeRoot)
    {
        using (var stream = new MemoryStream())
        {
            using (var writer = new Utf8JsonWriter(stream, WriterOptions))
            {
                writeRoot(writer);
            }

            stream.WriteByte((byte)'\n');
            return stream.ToArray();
        }
    }
}
}
