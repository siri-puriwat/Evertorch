namespace Evertorch.Server
{
public sealed class ContentOptions
{
    public const string SectionName = "Content";

    /// <summary>
    /// Directory holding the server package and its manifest. A relative path is resolved against the directory
    /// of the server executable.
    /// </summary>
    public string ServerPackagePath { get; set; } = "content/server";
}
}
