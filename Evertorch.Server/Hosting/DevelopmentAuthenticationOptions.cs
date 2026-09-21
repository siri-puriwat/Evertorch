namespace Evertorch.Server
{
/// <summary>
/// Stand-in for real session tokens until accounts exist. It is off unless configuration turns it on, and the
/// shipped default configuration does not.
/// </summary>
public sealed class DevelopmentAuthenticationOptions
{
    public const string SectionName = "DevelopmentAuthentication";

    /// <summary>
    /// When true, a token of the form <c>dev:name</c> is accepted as that identity. When false, every hello fails
    /// authentication.
    /// </summary>
    public bool Enabled { get; set; }
}
}
