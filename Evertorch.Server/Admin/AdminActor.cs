using System;

namespace Evertorch.Server
{
/// <summary>
///     Who issued an operator command, for its audit event (System Architecture §10). The local console counts as
///     authenticated by access to the server's own process, so it is named <c>console</c>, with the operating-system
///     user the process runs as.
/// </summary>
public readonly struct AdminActor
{
    public AdminActor(string name, string user)
    {
        Name = name;
        User = user;
    }

    public static AdminActor LocalConsole => new("console", Environment.UserName);

    public string Name { get; }

    public string User { get; }
}
}
