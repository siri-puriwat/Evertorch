using System;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BadHttpRequestException = Microsoft.AspNetCore.Http.BadHttpRequestException;

namespace Evertorch.Server
{
/// <summary>
///     The HTTPS gateway (System Architecture §3, §13): <c>POST /session</c> on Kestrel in a host of its own, answered
///     by <see cref="SignInService" />. Registered with the health endpoint, before <see cref="ServerLifetimeService" />,
///     so it starts before the transport and stops after it; until the transport admits connections, a sign-in is
///     answered 503.
/// </summary>
public sealed class GatewayEndpoint : IHostedService, IDisposable
{
    public const string SessionPath = "/session";

    private static readonly Action<ILogger, string, int, string, Exception?> LogListening =
        LoggerMessage.Define<string, int, string>(
            LogLevel.Information,
            new EventId(6007, "GatewayListening"),
            "Gateway listening on https://{Address}:{Port} with the certificate {Thumbprint}.");

    private readonly GatewayOptions m_options;
    private readonly int m_maxConnections;
    private readonly TimeProvider m_time;
    private readonly ILoggerFactory m_loggers;
    private readonly SignInService m_signIn;
    private readonly ILogger<GatewayEndpoint> m_logger;
    private WebApplication? m_app;
    private X509Certificate2? m_certificate;

    public GatewayEndpoint(
        IOptions<GatewayOptions> options,
        IOptions<NetworkOptions> network,
        TimeProvider time,
        ILoggerFactory loggers,
        SignInService signIn,
        ILogger<GatewayEndpoint> logger)
    {
        m_options = options.Value;
        m_maxConnections = network.Value.MaxConnections;
        m_time = time;
        m_loggers = loggers;
        m_signIn = signIn;
        m_logger = logger;
    }

    /// <summary>
    ///     The port actually bound, which differs from the configured one when that is 0; 0 while nothing listens.
    /// </summary>
    public int Port { get; private set; }

    /// <summary>
    ///     The SHA-1 thumbprint of the certificate served, public data a client may pin; empty while nothing listens.
    /// </summary>
    public string Thumbprint { get; private set; } = string.Empty;

    public void Dispose()
    {
        m_app?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        m_certificate?.Dispose();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!m_options.Enabled)
        {
            return;
        }

        // A certificate that cannot be found or a port that cannot be bound fails the server's start
        // (System Architecture §13).
        X509Certificate2 certificate = GatewayCertificate.Load(m_options, m_time.GetUtcNow().UtcDateTime);
        m_certificate = certificate;
        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxConcurrentConnections = m_maxConnections;
            kestrel.Limits.MaxConcurrentUpgradedConnections = m_maxConnections;
            kestrel.Limits.MaxRequestBodySize = SignInService.MaxBodyBytes;
            kestrel.Listen(
                IPAddress.Parse(m_options.BindAddress),
                m_options.Port,
                listen =>
                {
                    listen.Protocols = HttpProtocols.Http1;
                    listen.UseHttps(certificate);
                });
        });
        builder.Services.AddSingleton<IHostLifetime, EmbeddedHostLifetime>();
        builder.Services.AddSingleton(m_loggers);
        WebApplication app = builder.Build();
        app.Run(AnswerAsync);
        m_app = app;

        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        Port = new Uri(app.Urls.First()).Port;
        Thumbprint = certificate.Thumbprint;
        LogListening(m_logger, m_options.BindAddress, Port, Thumbprint, null);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (m_app != null)
        {
            await m_app.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsJson(string? contentType)
    {
        return MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? parsed)
            && string.Equals(parsed.MediaType, "application/json", StringComparison.OrdinalIgnoreCase);
    }

    // Reads at most one byte past the limit, so a longer body is known to be one without reading it all.
    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request)
    {
        byte[] buffer = new byte[SignInService.MaxBodyBytes + 1];
        int length = 0;
        try
        {
            int read;
            while (length < buffer.Length
                   && (read = await request.Body.ReadAsync(buffer.AsMemory(length), request.HttpContext.RequestAborted)
                       .ConfigureAwait(false))
                   > 0)
            {
                length += read;
            }
        }
        catch (BadHttpRequestException)
        {
            return null;
        }

        return length > SignInService.MaxBodyBytes ? null : buffer.AsSpan(0, length).ToArray();
    }

    private async Task AnswerAsync(HttpContext context)
    {
        HttpRequest request = context.Request;
        if (!HttpMethods.IsPost(request.Method) ||
            !string.Equals(request.Path.Value, SessionPath, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        byte[]? body = IsJson(request.ContentType) ? await ReadBodyAsync(request).ConfigureAwait(false) : null;
        SignInAnswer answer = body == null
            ? m_signIn.Malformed()
            : await m_signIn.SignInAsync(
                    body,
                    context.Connection.RemoteIpAddress ?? IPAddress.None,
                    request.Host.Host)
                .ConfigureAwait(false);
        context.Response.StatusCode = answer.StatusCode;
        context.Response.Headers.CacheControl = "no-store";
        if (answer.Body.Length > 0)
        {
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync(answer.Body, context.RequestAborted).ConfigureAwait(false);
        }
    }
}
}
