namespace Evertorch.Tools
{
public sealed class ContentPackages
{
    public ContentPackages(ContentPackage server, ContentPackage client)
    {
        Server = server;
        Client = client;
    }

    public ContentPackage Server { get; }

    public ContentPackage Client { get; }
}
}
