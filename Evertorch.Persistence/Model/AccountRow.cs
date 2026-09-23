using System;

namespace Evertorch.Persistence
{
internal sealed class AccountRow
{
    public long Id { get; set; }

    public string LoginNormalized { get; set; } = string.Empty;

    public string? PasswordHash { get; set; }

    public string? PasswordScheme { get; set; }

    public string Status { get; set; } = AccountStatus.Active;

    public DateTime CreatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }
}
}
