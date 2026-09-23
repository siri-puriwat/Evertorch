using System;
using Evertorch.Protocol;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Decides whether a hello may become a session. Checks run from the most general incompatibility to the most
///     specific, so a client learns the first thing it has to fix.
/// </summary>
public sealed class HandshakeValidator
{
    private readonly CompatibilityOptions m_compatibility;
    private readonly ISessionTokenValidator m_tokens;

    public HandshakeValidator(
        IOptions<CompatibilityOptions> compatibility,
        ISessionTokenValidator tokens,
        ServerContent content)
    {
        m_compatibility = compatibility.Value;
        m_tokens = tokens;
        if (!ContentVersionCodec.TryToWire(content.ClientContentVersion, out uint required))
        {
            throw new InvalidOperationException("The loaded client content version cannot be sent in a handshake.");
        }

        RequiredClientContentVersion = required;
    }

    public uint RequiredClientContentVersion { get; }

    /// <summary>
    ///     Returns <see cref="DisconnectReason.None" /> when the hello may go on to authentication: every version
    ///     matches and the token is well formed. Whether the token names an account is decided later, off the tick
    ///     thread.
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

        return m_tokens.IsWellFormed(hello.SessionToken)
            ? DisconnectReason.None
            : DisconnectReason.AuthenticationFailed;
    }
}
}
