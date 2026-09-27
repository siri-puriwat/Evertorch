using System;

namespace Evertorch.Persistence
{
/// <summary>
///     What the store keeps of session tokens (Persistence §4).
/// </summary>
public static class SessionTokenLimits
{
    /// <summary>
    ///     The length of a stored token hash: a SHA-256.
    /// </summary>
    public const int HashLength = 32;

    /// <summary>
    ///     Live tokens an account keeps; a sign-in beyond them deletes the oldest.
    /// </summary>
    public const int MaxLiveTokensPerAccount = 10;

    /// <summary>
    ///     How long an expired token stays, so a hello that presents it is told it expired rather than refused.
    /// </summary>
    public static readonly TimeSpan ExpiredRetention = TimeSpan.FromHours(24);
}
}
