using System;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Persistence;

namespace Evertorch.Server
{
/// <summary>
///     Sends a hello's token to the validator its form names (Network Protocol §4): <c>dev:</c> tokens to the
///     development one, which refuses them unless development sign-in is on, and everything else to the production
///     one.
/// </summary>
public sealed class SessionTokenValidator : ISessionTokenValidator
{
    private readonly DevelopmentTokenValidator m_development;
    private readonly ProductionTokenValidator m_production;

    public SessionTokenValidator(DevelopmentTokenValidator development, ProductionTokenValidator production)
    {
        m_development = development;
        m_production = production;
    }

    public bool IsWellFormed(string token)
    {
        return For(token).IsWellFormed(token);
    }

    public Task<SessionTokenCheck> ValidateAsync(string token, IGameStore store, CancellationToken cancellationToken)
    {
        return For(token).ValidateAsync(token, store, cancellationToken);
    }

    private ISessionTokenValidator For(string token)
    {
        return token.StartsWith(DevelopmentTokenValidator.TokenPrefix, StringComparison.Ordinal)
            ? m_development
            : m_production;
    }
}
}
