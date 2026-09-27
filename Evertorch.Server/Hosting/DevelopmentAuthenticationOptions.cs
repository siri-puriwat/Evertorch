namespace Evertorch.Server
{
/// <summary>
///     The development sign-in, kept for the automated tests' bare connections (Network Protocol §4). It is off unless
///     configuration turns it on, and no shipped configuration does.
/// </summary>
public sealed class DevelopmentAuthenticationOptions
{
    public const string SectionName = "DevelopmentAuthentication";

    /// <summary>
    ///     When true, a token of the form <c>dev:name</c> is accepted as that identity. When false, every hello fails
    ///     authentication.
    /// </summary>
    public bool Enabled { get; set; }
}
}
