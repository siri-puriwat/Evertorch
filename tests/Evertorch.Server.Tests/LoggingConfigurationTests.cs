using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The console is the only log sink, with UTC timestamps, the simple formatter unless configuration asks for JSON,
///     and the audit category's level kept apart from the rest of Evertorch (System Architecture §10).
/// </summary>
[TestFixture]
public sealed class LoggingConfigurationTests
{
    private static IHost Build(params string[] args)
    {
        // The test output carries the server's appsettings files, so this is the configuration the server ships with.
        return TestHosts.CreateBuilder(args, AppContext.BaseDirectory).Build();
    }

    [Test]
    public void Host_ByDefault_LogsToTheConsoleOnly()
    {
        using IHost host = Build();

        ILoggerProvider[] providers = host.Services.GetServices<ILoggerProvider>().ToArray();

        Assert.That(providers, Has.Length.EqualTo(1));
        Assert.That(providers[0], Is.InstanceOf<ConsoleLoggerProvider>());
    }

    [Test]
    public void Host_ByDefault_WritesTheSimpleFormatWithUtcTimestamps()
    {
        using IHost host = Build();

        ConsoleLoggerOptions console =
            host.Services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue;
        SimpleConsoleFormatterOptions simple =
            host.Services.GetRequiredService<IOptionsMonitor<SimpleConsoleFormatterOptions>>().CurrentValue;

        // Named, not left null: without a name the provider formats with its deprecated options and no timestamp.
        Assert.That(console.FormatterName, Is.EqualTo(ConsoleFormatterNames.Simple));
        Assert.That(simple.UseUtcTimestamp, Is.True);
        Assert.That(simple.TimestampFormat, Does.StartWith(ServerHost.UtcTimestampFormat));
    }

    [Test]
    public void Host_InDevelopment_KeepsTheAuditCategoryAtInformation()
    {
        using IHost host = Build("--environment=Development");
        ILoggerFactory loggers = host.Services.GetRequiredService<ILoggerFactory>();

        ILogger audit = loggers.CreateLogger(LogCategories.Audit);
        ILogger server = loggers.CreateLogger(typeof(SessionManager).FullName!);

        Assert.That(server.IsEnabled(LogLevel.Debug), Is.True, "development runs Evertorch at Debug");
        Assert.That(audit.IsEnabled(LogLevel.Debug), Is.False, "refusals would flood the console");
        Assert.That(audit.IsEnabled(LogLevel.Information), Is.True);
    }

    [Test]
    public void Host_WithJsonFormatterName_WritesJsonWithUtcTimestamps()
    {
        using IHost host = Build("--Logging:Console:FormatterName=json");

        ConsoleLoggerOptions console =
            host.Services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue;
        JsonConsoleFormatterOptions json =
            host.Services.GetRequiredService<IOptionsMonitor<JsonConsoleFormatterOptions>>().CurrentValue;

        Assert.That(console.FormatterName, Is.EqualTo(ConsoleFormatterNames.Json));
        Assert.That(json.UseUtcTimestamp, Is.True);
        Assert.That(json.TimestampFormat, Is.EqualTo(ServerHost.UtcTimestampFormat));
    }
}
}
