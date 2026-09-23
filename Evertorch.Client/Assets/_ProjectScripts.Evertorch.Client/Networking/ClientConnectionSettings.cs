using System;

namespace Evertorch.Client
{
public sealed class ClientConnectionSettings
{
    public ClientConnectionSettings(string buildVersion, string contentVersion, string sessionToken)
    {
        BuildVersion = buildVersion ?? throw new ArgumentNullException(nameof(buildVersion));
        ContentVersion = contentVersion ?? throw new ArgumentNullException(nameof(contentVersion));
        SessionToken = sessionToken ?? throw new ArgumentNullException(nameof(sessionToken));
    }

    public string BuildVersion { get; }

    /// <summary>
    ///     The client package's content version as its manifest states it.
    /// </summary>
    public string ContentVersion { get; }

    public string SessionToken { get; }
}
}
