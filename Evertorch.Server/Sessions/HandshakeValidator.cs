using System;
using Evertorch.Protocol;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
/// Decides whether a hello may become a session. Checks run from the most general incompatibility to the most
/// specific, so a client learns the first thing it has to fix.
/// </summary>
public sealed class HandshakeValidator
{
    private const string DevelopmentTokenPrefix = "dev:";
    private const int MaxDevelopmentIdentityLength = 64;

    private readonly CompatibilityOptions m_compatibility;
    private readonly bool m_isDevelopmentAuthenticationEnabled;

    public HandshakeValidator(
        IOptions<CompatibilityOptions> compatibility,
        IOptions<DevelopmentAuthenticationOptions> developmentAuthentication,
        ServerContent content)
    {
        m_compatibility = compatibility.Value;
        m_isDevelopmentAuthenticationEnabled = developmentAuthentication.Value.Enabled;
        if (!ContentVersionCodec.TryToWire(content.ClientContentVersion, out uint required))
        {
            throw new InvalidOperationException("The loaded client content version cannot be sent in a handshake.");
        }

        RequiredClientContentVersion = required;
    }

    public uint RequiredClientContentVersion { get; }

    /// <summary>
    /// Returns <see cref="DisconnectReason.None"/> when the hello is accepted.
    /// </summary>
    public DisconnectReason Validate(ClientHello hello)
    {
        if (hello.ProtocolVersion != ProtocolConstants.ProtocolVersion)
        {
            return DisconnectReason.ProtocolMismatch;
        }

        if (!m_compatibility.Accepts(hello.ClientBuildVersion))
        {
            return DisconnectReason.ClientBuildUnsupported;
        }

        if (hello.ClientContentVersion != RequiredClientContentVersion)
        {
            return DisconnectReason.ContentUpdateRequired;
        }

        return IsAcceptedToken(hello.SessionToken) ? DisconnectReason.None : DisconnectReason.AuthenticationFailed;
    }

    // Real session tokens arrive with accounts. Until then only an explicitly enabled development identity passes.
    private bool IsAcceptedToken(string token)
    {
        if (!m_isDevelopmentAuthenticationEnabled
            || !token.StartsWith(DevelopmentTokenPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        int identityLength = token.Length - DevelopmentTokenPrefix.Length;
        if (identityLength < 1 || identityLength > MaxDevelopmentIdentityLength)
        {
            return false;
        }

        for (int index = DevelopmentTokenPrefix.Length; index < token.Length; index++)
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
}
}
