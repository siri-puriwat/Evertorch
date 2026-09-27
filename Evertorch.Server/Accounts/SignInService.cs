using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     <c>POST /session</c> without its HTTP (Network Protocol §4): checks a login and a password and issues a session
///     token with where to connect. It calls the store directly, outside the persistence writer, touching only accounts
///     and their tokens (Persistence §9). Every credential refusal is answered alike and costs one password hash. No log
///     names a login, a password, a token, or an address.
/// </summary>
public sealed class SignInService
{
    public const int MaxBodyBytes = 1024;
    public const string UdpTransport = "udp";

    public const string IssuedOutcome = "issued";
    public const string RefusedOutcome = "refused";
    public const string ThrottledOutcome = "throttled";
    public const string UnavailableOutcome = "unavailable";
    public const string MalformedOutcome = "malformed";

    private const string CredentialsReason = "credentials";

    private static readonly Action<ILogger, long, int, Exception?> LogSignedIn =
        LoggerMessage.Define<long, int>(
            LogLevel.Information,
            new EventId(2009, "SignedIn"),
            "Account {Account} signed in; its session token lasts {LifetimeSeconds} s.");

    private readonly IGameStore m_store;
    private readonly PasswordHasher m_hasher;
    private readonly TimeProvider m_time;
    private readonly HealthProbe m_health;
    private readonly SignInThrottle m_throttle;
    private readonly IServerTransport m_transport;
    private readonly ServerInstruments m_instruments;
    private readonly AuditLog m_audit;
    private readonly ILogger<SignInService> m_logger;
    private readonly SemaphoreSlim m_signIns;
    private readonly int m_timeoutMs;
    private readonly TimeSpan m_tokenLifetime;
    private readonly string? m_answeredHost;

    public SignInService(
        IGameStore store,
        PasswordHasher hasher,
        TimeProvider time,
        HealthProbe health,
        SignInThrottle throttle,
        IServerTransport transport,
        ServerInstruments instruments,
        AuditLog audit,
        IOptions<GatewayOptions> gateway,
        IOptions<NetworkOptions> network,
        IOptions<PersistenceOptions> persistence,
        IOptions<SessionOptions> session,
        ILogger<SignInService> logger)
    {
        m_store = store;
        m_hasher = hasher;
        m_time = time;
        m_health = health;
        m_throttle = throttle;
        m_transport = transport;
        m_instruments = instruments;
        m_audit = audit;
        m_logger = logger;
        m_signIns = new SemaphoreSlim(gateway.Value.MaxConcurrentSignIns);
        m_timeoutMs = persistence.Value.CommandTimeoutMs;
        m_tokenLifetime = TimeSpan.FromMilliseconds(session.Value.TokenLifetimeMs);
        m_answeredHost = gateway.Value.PublicHost ?? SpecificAddress(network.Value.BindAddress);
    }

    /// <summary>
    ///     Answers a sign-in whose body could not be read as one: too long, or not JSON by its content type.
    /// </summary>
    public SignInAnswer Malformed()
    {
        m_instruments.RecordSignIn(MalformedOutcome);
        return SignInAnswer.Status(400);
    }

    /// <param name="body">The request's JSON, at most <see cref="MaxBodyBytes" />.</param>
    /// <param name="remote">The caller's address, for its limit only.</param>
    /// <param name="requestHost">The host the request named, answered when nothing more specific is known.</param>
    public async Task<SignInAnswer> SignInAsync(ReadOnlyMemory<byte> body, IPAddress remote, string requestHost)
    {
        if (!m_throttle.TryAdmitAddress(remote))
        {
            return Throttled(ServerInstruments.SignInAddressLimit, null);
        }

        if (!m_health.Evaluate().IsReady || !m_signIns.Wait(0))
        {
            m_instruments.RecordSignIn(UnavailableOutcome);
            return SignInAnswer.Status(503);
        }

        try
        {
            return TryRead(body.Span, out string login, out string password)
                ? await CheckAsync(login, password, requestHost).ConfigureAwait(false)
                : Malformed();
        }
        finally
        {
            m_signIns.Release();
        }
    }

    // The request: an object with the login, the password, and the transports the client can use, which must name
    // one this server serves. Other members are ignored, and none may repeat.
    private static bool TryRead(ReadOnlySpan<byte> body, out string login, out string password)
    {
        login = string.Empty;
        password = string.Empty;
        string? foundLogin = null;
        string? foundPassword = null;
        bool? offersUdp = null;
        try
        {
            var reader = new Utf8JsonReader(body, new JsonReaderOptions { MaxDepth = 4 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string name = reader.GetString()!;
                if (!names.Add(name) || !reader.Read())
                {
                    return false;
                }

                switch (name)
                {
                    case "login" when reader.TokenType == JsonTokenType.String:
                        foundLogin = reader.GetString();
                        break;
                    case "password" when reader.TokenType == JsonTokenType.String:
                        foundPassword = reader.GetString();
                        break;
                    case "transports" when reader.TokenType == JsonTokenType.StartArray:
                        offersUdp = ReadsUdp(ref reader);
                        break;
                    case "login":
                    case "password":
                    case "transports":
                        return false;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
            {
                return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (foundLogin == null || foundPassword == null || offersUdp != true)
        {
            return false;
        }

        login = foundLogin;
        password = foundPassword;
        return true;
    }

    private static bool ReadsUdp(ref Utf8JsonReader reader)
    {
        bool offersUdp = false;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("A transport is a string.");
            }

            offersUdp |= reader.ValueTextEquals(UdpTransport);
        }

        return offersUdp;
    }

    private static string? SpecificAddress(string bindAddress)
    {
        return IPAddress.TryParse(bindAddress, out IPAddress? address)
            && !address.Equals(IPAddress.Any)
            && !address.Equals(IPAddress.IPv6Any)
                ? bindAddress
                : null;
    }

    private static byte[] Answer(string host, int port, string token)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("transport", UdpTransport);
            writer.WriteString("host", host);
            writer.WriteNumber("port", port);
            writer.WriteString("token", token);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private async Task<SignInAnswer> CheckAsync(string login, string password, string requestHost)
    {
        bool isLogin = AccountCredentialRules.TryNormalizeLogin(login, out string normalized);
        string limited = isLogin ? normalized : login.ToLowerInvariant();
        if (!m_throttle.MayTry(limited))
        {
            return Throttled(ServerInstruments.SignInLoginLimit, null);
        }

        AccountCredentials? credentials = null;
        if (isLogin)
        {
            try
            {
                using var timeout = new CancellationTokenSource(m_timeoutMs);
                credentials = await m_store.FindAccountCredentialsAsync(normalized, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (StoreUnavailableException)
            {
                m_instruments.RecordSignIn(UnavailableOutcome);
                return SignInAnswer.Status(503);
            }
        }

        // Always one hash, so the time taken tells nothing about why a refusal was made.
        bool isPassword = m_hasher.Verify(password, credentials?.PasswordScheme, credentials?.PasswordHash);
        if (!isPassword || credentials == null || credentials.IsDisabled)
        {
            m_throttle.RecordFailure(limited);
            m_instruments.RecordSignIn(RefusedOutcome);
            m_audit.SignInRefused(CredentialsReason, credentials?.Account);
            return SignInAnswer.Status(401);
        }

        var token = SessionToken.Create();
        DateTime now = m_time.GetUtcNow().UtcDateTime;
        try
        {
            using var timeout = new CancellationTokenSource(m_timeoutMs);
            await m_store.IssueSessionTokenAsync(credentials.Account, token.Hash, now, now + m_tokenLifetime,
                    timeout.Token)
                .ConfigureAwait(false);
        }
        catch (StoreUnavailableException)
        {
            m_instruments.RecordSignIn(UnavailableOutcome);
            return SignInAnswer.Status(503);
        }

        m_instruments.RecordSignIn(IssuedOutcome);
        LogSignedIn(m_logger, credentials.Account.Value, (int)m_tokenLifetime.TotalSeconds, null);
        return SignInAnswer.Json(Answer(m_answeredHost ?? requestHost, m_transport.LocalPort, token.Text));
    }

    private SignInAnswer Throttled(string limit, AccountId? account)
    {
        m_instruments.RecordRateLimited(limit);
        m_instruments.RecordSignIn(ThrottledOutcome);
        m_audit.SignInRefused(limit, account);
        return SignInAnswer.Status(429);
    }
}

/// <summary>
///     A sign-in's HTTP answer: its status, and the JSON body of a token, empty for every refusal.
/// </summary>
public sealed class SignInAnswer
{
    private SignInAnswer(int statusCode, byte[] body)
    {
        StatusCode = statusCode;
        Body = body;
    }

    public int StatusCode { get; }

    public byte[] Body { get; }

    public static SignInAnswer Status(int statusCode)
    {
        return new SignInAnswer(statusCode, Array.Empty<byte>());
    }

    public static SignInAnswer Json(byte[] body)
    {
        return new SignInAnswer(200, body);
    }
}
}
