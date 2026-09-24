using System;
using System.IO;
using Evertorch.Persistence;
using Evertorch.Rules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
public static class ServerHost
{
    public const string UtcTimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    /// <summary>
    ///     Builds the server composition. Configuration comes from <c>appsettings.json</c> and
    ///     <c>appsettings.{Environment}.json</c> under <paramref name="contentRootPath" />, then environment variables,
    ///     then the command line; later sources win.
    /// </summary>
    public static HostApplicationBuilder CreateBuilder(string[] args, string contentRootPath)
    {
        var settings = new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = contentRootPath
        };
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(settings);
        AddLogging(builder);

        builder.Services
            .AddOptions<SimulationOptions>()
            .Bind(builder.Configuration.GetSection(SimulationOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<SimulationOptions>, SimulationOptionsValidator>();
        builder.Services
            .AddOptions<ContentOptions>()
            .Bind(builder.Configuration.GetSection(ContentOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<ContentOptions>, ContentOptionsValidator>();

        // Resolved while the lifetime service is constructed, so a bad package stops the host before any thread or
        // socket exists.
        builder.Services.AddSingleton(services => LoadContent(services, contentRootPath));

        builder.Services
            .AddOptions<DatabaseOptions>()
            .Configure(options => options.ConnectionString =
                builder.Configuration.GetConnectionString(DatabaseOptions.ConnectionName) ?? string.Empty)
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
        builder.Services.AddSingleton<IGameStore>(services =>
            new PostgresGameStore(
                services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString,
                TimeSpan.FromMilliseconds(services.GetRequiredService<IOptions<PersistenceOptions>>().Value
                    .CommandTimeoutMs)));
        AddOptions<PersistenceOptions, PersistenceOptionsValidator>(builder, PersistenceOptions.SectionName);
        AddOptions<SessionOptions, SessionOptionsValidator>(builder, SessionOptions.SectionName);
        builder.Services.AddSingleton<PersistenceWorker>();

        // Registered before every other hosted service, so it runs before the simulation starts.
        builder.Services.AddHostedService<DatabaseStartupCheck>();

        AddOptions<NetworkOptions, NetworkOptionsValidator>(builder, NetworkOptions.SectionName);
        AddOptions<HealthOptions, HealthOptionsValidator>(builder, HealthOptions.SectionName);
        AddOptions<AbuseOptions, AbuseOptionsValidator>(builder, AbuseOptions.SectionName);
        AddOptions<CompatibilityOptions, CompatibilityOptionsValidator>(builder, CompatibilityOptions.SectionName);
        AddOptions<WorldOptions, WorldOptionsValidator>(builder, WorldOptions.SectionName);
        builder.Services
            .AddOptions<DevelopmentAuthenticationOptions>()
            .Bind(builder.Configuration.GetSection(DevelopmentAuthenticationOptions.SectionName));

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ICharacterRules, RenewalCharacterRules>();
        builder.Services.AddSingleton<IMovementRules, RenewalMovementRules>();
        builder.Services.AddSingleton<ICombatRules, RenewalCombatRules>();
        builder.Services.AddSingleton(services =>
            ServerRandom.FromOptions(services.GetRequiredService<IOptions<WorldOptions>>().Value));
        builder.Services.AddSingleton<IRandomSource>(services => services.GetRequiredService<ServerRandom>());

        builder.Services.AddSingleton<InboundQueue>();
        builder.Services.AddSingleton<SessionRegistry>();
        builder.Services.AddSingleton<ISessionTokenValidator, DevelopmentTokenValidator>();
        builder.Services.AddSingleton<HandshakeValidator>();
        builder.Services.AddSingleton<WorldSimulation>();
        builder.Services.AddSingleton<MessageSender>();
        builder.Services.AddSingleton<Targeting>();
        builder.Services.AddSingleton<PlayerLife>();
        builder.Services.AddSingleton<CharacterLifetime>();
        builder.Services.AddSingleton<PickupSystem>();
        builder.Services.AddSingleton<ITickPhase>(services => services.GetRequiredService<PickupSystem>());
        builder.Services.AddSingleton<AddressThrottle>();
        builder.Services.AddSingleton<LiteNetLibServerTransport>();
        builder.Services.AddSingleton<IServerTransport>(services =>
            services.GetRequiredService<LiteNetLibServerTransport>());
        builder.Services.AddSingleton<IOutboundMessages>(services => services.GetRequiredService<IServerTransport>());
        builder.Services.AddSingleton<ITransportStatistics>(services =>
            services.GetRequiredService<IServerTransport>());
        builder.Services.AddSingleton<SessionManager>();
        builder.Services.AddSingleton<ITickPhase>(services => services.GetRequiredService<SessionManager>());
        builder.Services.AddSingleton<ITickPhase, MovementSystem>();
        builder.Services.AddSingleton<CombatSystem>();
        builder.Services.AddSingleton<ITickPhase>(services => services.GetRequiredService<CombatSystem>());
        builder.Services.AddSingleton<ITickPhase, MonsterAiSystem>();
        builder.Services.AddSingleton<ItemDropSystem>();
        builder.Services.AddSingleton<ITickPhase>(services => services.GetRequiredService<ItemDropSystem>());
        builder.Services.AddSingleton<ITickPhase, VisibilityPhase>();
        builder.Services.AddSingleton<ITickPhase, InventorySyncPhase>();
        builder.Services.AddSingleton<ITickPhase, SnapshotPhase>();
        builder.Services.AddSingleton<ITickPhase, CheckpointScheduler>();
        builder.Services.AddSingleton<AdminQueue>();
        builder.Services.AddSingleton<ITickPhase>(services => services.GetRequiredService<AdminQueue>());
        builder.Services.AddSingleton<StatusPublisher>();
        builder.Services.AddSingleton<ITickPhase>(services => services.GetRequiredService<StatusPublisher>());

        builder.Services.AddSingleton<ShutdownRequest>();
        builder.Services.AddSingleton<IAdminCommandService, AdminCommandService>();
        builder.Services.AddSingleton<AdminConsole>();
        builder.Services.AddHostedService<ConsoleCommandService>();

        builder.Services.AddSingleton<IMonotonicClock, StopwatchClock>();
        builder.Services.AddSingleton<ServerInstruments>();
        builder.Services.AddSingleton(services => new AuditLog(
            services.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategories.Audit),
            services.GetRequiredService<IMonotonicClock>()));
        builder.Services.AddSingleton<TickLogObserver>();
        builder.Services.AddSingleton<ServerMetrics>();
        builder.Services.AddSingleton<ITickObserver>(services => services.GetRequiredService<ServerMetrics>());
        builder.Services.AddSingleton<TickPipeline>();
        builder.Services.AddSingleton<FixedStepLoop>();
        // Before the lifetime service, so the endpoints start before the transport and stop after everything else.
        builder.Services.AddSingleton<HealthEndpoint>();
        builder.Services.AddHostedService(services => services.GetRequiredService<HealthEndpoint>());
        builder.Services.AddSingleton<ServerLifetimeService>();
        builder.Services
            .AddOptions<HostOptions>()
            .Configure<IOptions<PersistenceOptions>>((host, persistence) =>
                host.ShutdownTimeout = ServerLifetimeService.ShutdownTimeout(persistence.Value));
        builder.Services.AddHostedService(services => services.GetRequiredService<ServerLifetimeService>());

        return builder;
    }

    // The console is the only sink: the default host also adds Debug, EventSource, and on Windows EventLog. The
    // formatter comes from Logging:Console:FormatterName, so only the formatters' options are set here;
    // AddSimpleConsole and AddJsonConsole would each fix the formatter name as well. Without a name the provider
    // formats with its deprecated options instead and writes no timestamp, so simple is named explicitly.
    private static void AddLogging(HostApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Services.Configure<ConsoleLoggerOptions>(options =>
            options.FormatterName ??= ConsoleFormatterNames.Simple);
        builder.Services.Configure<SimpleConsoleFormatterOptions>(options =>
        {
            options.UseUtcTimestamp = true;
            options.TimestampFormat = $"{UtcTimestampFormat} ";
        });
        builder.Services.Configure<JsonConsoleFormatterOptions>(options =>
        {
            options.UseUtcTimestamp = true;
            options.TimestampFormat = UtcTimestampFormat;
        });
    }

    private static void AddOptions<TOptions, TValidator>(HostApplicationBuilder builder, string sectionName)
        where TOptions : class
        where TValidator : class, IValidateOptions<TOptions>
    {
        builder.Services.AddOptions<TOptions>().Bind(builder.Configuration.GetSection(sectionName)).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<TOptions>, TValidator>();
    }

    private static ServerContent LoadContent(IServiceProvider services, string contentRootPath)
    {
        ContentOptions options = services.GetRequiredService<IOptions<ContentOptions>>().Value;
        return ServerContentLoader.LoadFromDirectory(Path.GetFullPath(options.ServerPackagePath, contentRootPath));
    }
}
}
