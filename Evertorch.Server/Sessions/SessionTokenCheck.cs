using Evertorch.Persistence;

namespace Evertorch.Server
{
public enum SessionTokenVerdict
{
    Refused,
    Accepted,
    Expired
}

/// <summary>
///     What a hello's token turned out to be (Network Protocol §4): an account's, refused, or a known token past its
///     expiry.
/// </summary>
public sealed class SessionTokenCheck
{
    public static readonly SessionTokenCheck Refused = new(SessionTokenVerdict.Refused, null);
    public static readonly SessionTokenCheck Expired = new(SessionTokenVerdict.Expired, null);

    private SessionTokenCheck(SessionTokenVerdict verdict, AccountId? account)
    {
        Verdict = verdict;
        Account = account;
    }

    public SessionTokenVerdict Verdict { get; }

    /// <summary>
    ///     The signed-in account; set only when <see cref="Verdict" /> is <see cref="SessionTokenVerdict.Accepted" />.
    /// </summary>
    public AccountId? Account { get; }

    public static SessionTokenCheck Accepted(AccountId account)
    {
        return new SessionTokenCheck(SessionTokenVerdict.Accepted, account);
    }
}
}
