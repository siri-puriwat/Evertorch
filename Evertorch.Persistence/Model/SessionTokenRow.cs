using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One session token (Persistence §4), stored only as the SHA-256 of its bytes.
/// </summary>
internal sealed class SessionTokenRow
{
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();

    public long AccountId { get; set; }

    public DateTime IssuedAt { get; set; }

    public DateTime ExpiresAt { get; set; }
}
}
