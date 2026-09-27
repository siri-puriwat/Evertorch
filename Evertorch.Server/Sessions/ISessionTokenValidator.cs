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
///     <see cref="SessionTokenValidator" /> sends each token to <see cref="DevelopmentTokenValidator" /> or
///     <see cref="ProductionTokenValidator" /> by its form.
/// </remarks>
public interface ISessionTokenValidator
{
    /// <summary>
    ///     Tick thread, before any database work: whether the token could be valid at all. A false answer refuses the
    ///     hello with <c>AuthenticationFailed</c>.
    /// </summary>
    bool IsWellFormed(string token);

    /// <summary>
    ///     Persistence writer: the account the token belongs to, or why it has none.
    /// </summary>
    Task<SessionTokenCheck> ValidateAsync(string token, IGameStore store, CancellationToken cancellationToken);
}
}
