using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Serves <c>/health/live</c> and <c>/health/ready</c> over plain HTTP (System Architecture §10), with Kestrel in a
///     host of its own; <see cref="HealthProbe" /> decides the answers.
/// </summary>
/// <remarks>
///     It is registered before <see cref="ServerLifetimeService" />, so it starts before the transport and stops after
///     everything else. Its host has no lifetime, so Ctrl+C reaches only the server's, and it logs through the server's
///     logger factory.
/// </remarks>
public sealed class HealthEndpoint : IHostedService, IDisposable
{
    private const string LivePath = "/health/live";
    private const string ReadyPath = "/health/ready";

    private static readonly Action<ILogger, string, int, Exception?> LogListening =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(6003, "HealthListening"),
            "Health endpoints listening on http://{Address}:{Port}.");

    private readonly HealthOptions m_options;
    private readonly ILoggerFactory m_loggers;
    private readonly HealthProbe m_probe;
    private readonly ILogger<HealthEndpoint> m_logger;
    private WebApplication? m_app;

    public HealthEndpoint(
        IOptions<HealthOptions> options,
        ILoggerFactory loggers,
        HealthProbe probe,
        ILogger<HealthEndpoint> logger)
    {
        m_options = options.Value;
        m_loggers = loggers;
        m_probe = probe;
        m_logger = logger;
    }

    /// <summary>
    ///     The port actually bound, which differs from the configured one when that is 0; 0 while nothing listens.
    /// </summary>
    public int Port { get; private set; }

    public void Dispose()
    {
        m_app?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!m_options.Enabled)
        {
            return;
        }

        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore();
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(IPAddress.Parse(m_options.BindAddress), m_options.Port));
        builder.Services.AddSingleton<IHostLifetime, EmbeddedHostLifetime>();
        builder.Services.AddSingleton(m_loggers);
        WebApplication app = builder.Build();
        app.Run(AnswerAsync);
        m_app = app;

        // A port that cannot be bound fails the server's start, like the game port (System Architecture §13).
        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        Port = new Uri(app.Urls.First()).Port;
        LogListening(m_logger, m_options.BindAddress, Port, null);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (m_app != null)
        {
            await m_app.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AnswerAsync(HttpContext context)
    {
        string path = context.Request.Path.Value ?? string.Empty;
        bool isLivePath = string.Equals(path, LivePath, StringComparison.Ordinal);
        if (!isLivePath && !string.Equals(path, ReadyPath, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        HealthReport report = m_probe.Evaluate();
        bool isHealthy = isLivePath ? report.IsLive : report.IsReady;
        context.Response.StatusCode = isHealthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.Body.WriteAsync(report.ToJson(), context.RequestAborted).ConfigureAwait(false);
    }
}
}
