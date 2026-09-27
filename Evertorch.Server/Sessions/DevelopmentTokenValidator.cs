using System;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Persistence;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Accepts <c>dev:&lt;identity&gt;</c> tokens while development authentication is enabled, and provisions the
///     identity's account on first use (Persistence §4). Identities differing only in letter case are one account.
/// </summary>
public sealed class DevelopmentTokenValidator : ISessionTokenValidator
{
    public const string TokenPrefix = "dev:";
    public const int MaxIdentityLength = 64;

    private readonly bool m_isEnabled;
    private readonly TimeProvider m_time;

    public DevelopmentTokenValidator(IOptions<DevelopmentAuthenticationOptions> options, TimeProvider time)
    {
        m_isEnabled = options.Value.Enabled;
        m_time = time;
    }

    public bool IsWellFormed(string token)
    {
        if (!m_isEnabled || !token.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        int identityLength = token.Length - TokenPrefix.Length;
        if (identityLength < 1 || identityLength > MaxIdentityLength)
        {
            return false;
        }

        for (int index = TokenPrefix.Length; index < token.Length; index++)
        {
            char character = token[index];
            bool isAllowed = (character >= 'a' && character <= 'z')
                || (character >= 'A' && character <= 'Z')
                || (character >= '0' && character <= '9')
                || character == '_'
                || character == '-'
                || character == '.';
            if (!isAllowed)
            {
                return false;
            }
        }

        return true;
    }

    public async Task<SessionTokenCheck> ValidateAsync(
        string token,
        IGameStore store,
        CancellationToken cancellationToken)
    {
        if (!IsWellFormed(token))
        {
            return SessionTokenCheck.Refused;
        }

        AccountId? account = await store
            .ProvisionAccountAsync(NormalizedLogin(token), m_time.GetUtcNow().UtcDateTime, cancellationToken)
            .ConfigureAwait(false);
        return account is AccountId found ? SessionTokenCheck.Accepted(found) : SessionTokenCheck.Refused;
    }

    /// <summary>
    ///     The stored login of <paramref name="token" />: its identity in lower case behind the prefix, so development
    ///     logins can never collide with future real ones.
    /// </summary>
    public static string NormalizedLogin(string token)
    {
        return $"{TokenPrefix}{token.Substring(TokenPrefix.Length).ToLowerInvariant()}";
    }
}
}
