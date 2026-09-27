using System;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Persistence;

namespace Evertorch.Server
{
/// <summary>
///     Accepts the session tokens the gateway issues (Network Protocol §4): a known token of an active account until
///     it expires. A known token past its expiry is <see cref="SessionTokenVerdict.Expired" />, so the client signs in
///     again rather than being told its credentials are wrong.
/// </summary>
public sealed class ProductionTokenValidator : ISessionTokenValidator
{
    private readonly TimeProvider m_time;

    public ProductionTokenValidator(TimeProvider time)
    {
        m_time = time;
    }

    public bool IsWellFormed(string token)
    {
        return SessionToken.TryParse(token, out _);
    }

    public async Task<SessionTokenCheck> ValidateAsync(
        string token,
        IGameStore store,
        CancellationToken cancellationToken)
    {
        if (!SessionToken.TryParse(token, out byte[] hash))
        {
            return SessionTokenCheck.Refused;
        }

        StoredSessionToken? stored = await store.FindSessionTokenAsync(hash, cancellationToken).ConfigureAwait(false);
        if (stored == null || stored.IsAccountDisabled)
        {
            return SessionTokenCheck.Refused;
        }

        return stored.ExpiresAt > m_time.GetUtcNow().UtcDateTime
            ? SessionTokenCheck.Accepted(stored.Account)
            : SessionTokenCheck.Expired;
    }
}
}
