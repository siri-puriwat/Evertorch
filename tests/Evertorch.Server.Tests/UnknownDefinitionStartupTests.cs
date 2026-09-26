using System;
using System.Linq;
using System.Threading;
using Evertorch.Game;
using Evertorch.Persistence;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Startup compares the definitions stored characters refer to with the loaded content (Persistence §8).
/// </summary>
[TestFixture]
public sealed class UnknownDefinitionStartupTests
{
    private static (DatabaseStartupCheck Check, CapturingLogger<DatabaseStartupCheck> Log) Create(
        InMemoryGameStore store)
    {
        ServerContent content = new TestServer().Content;
        var worker = new PersistenceWorker(
            store,
            Options.Create(new PersistenceOptions()),
            TestInstruments.Create(),
            new CapturingLogger<PersistenceWorker>());
        var log = new CapturingLogger<DatabaseStartupCheck>();
        return (new DatabaseStartupCheck(worker, store, content, log), log);
    }

    private static void Seed(InMemoryGameStore store, string? job = null, string? item = null, string? quest = null)
    {
        AccountId account = store.ProvisionAccountAsync("dev:seed", DateTime.UtcNow, CancellationToken.None).Result!
            .Value;
        var character = new NewCharacter(
            "Seeded",
            new JobDefinitionId("job.adventurer"),
            new PrimaryStats(1, 1, 1, 1, 1, 1),
            10,
            10,
            new MapDefinitionId("map.training_ground"),
            new WorldPosition(0f, 0f, 0f),
            DateTime.UtcNow);
        long id = store.CreateCharacterAsync(account, character, 3, CancellationToken.None).Result.CharacterId;
        store.Edit(id, job, item: item);
        if (quest != null)
        {
            store.GiveQuest(id, quest, 1);
        }
    }

    [Test]
    public void Start_WithOnlyKnownDefinitions_WarnsNothing()
    {
        var store = new InMemoryGameStore();
        Seed(store, item: "item.material.slime_gel", quest: "quest.crawler_hunt");
        (DatabaseStartupCheck check, CapturingLogger<DatabaseStartupCheck> log) = Create(store);

        check.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(log.Entries, Is.Empty);
    }

    [Test]
    public void Start_WithStoredDefinitionsTheContentLacks_WarnsNamingThemAndStillStarts()
    {
        var store = new InMemoryGameStore();
        Seed(store, "job.retired", "item.material.retired", "quest.retired");
        (DatabaseStartupCheck check, CapturingLogger<DatabaseStartupCheck> log) = Create(store);

        check.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        string warning = log.Entries.Single(entry => entry.EventId.Name == "UnknownPersistedDefinitions").Message;
        Assert.That(
            warning,
            Does.Contain("job.retired").And.Contain("item.material.retired").And.Contain("quest.retired"));
        Assert.That(warning, Does.Not.Contain("map.training_ground"));
    }
}
}
