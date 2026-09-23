using System.Threading;
using System.Threading.Tasks;
using Evertorch.Persistence;

namespace Evertorch.Server
{
/// <summary>
///     Turns the session token of a <c>ClientHello</c> into an account (Network Protocol §4). The token is checked
///     here and dropped: no session keeps it and no log names it.
/// </summary>
/// <remarks>
///     Milestone 4 has only <see cref="DevelopmentTokenValidator" />. A production validator for the short-lived
///     tokens of the HTTPS endpoint (an opaque 256-bit value stored hashed with its account and expiry) implements the
///     same interface when that endpoint exists.
/// </remarks>
public interface ISessionTokenValidator
{
    /// <summary>
    ///     Tick thread, before any database work: whether the token could be valid at all. A false answer refuses the
    ///     hello with <c>AuthenticationFailed</c>.
    /// </summary>
    bool IsWellFormed(string token);

    /// <summary>
    ///     Persistence writer: the account the token belongs to, or null when it is refused.
    /// </summary>
    Task<AccountId?> ValidateAsync(string token, IGameStore store, CancellationToken cancellationToken);
}
}
