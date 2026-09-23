using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Characters are listed and created through the protocol (Network Protocol §4, Persistence §4).
/// </summary>
[TestFixture]
public sealed class CharacterSelectionTests
{
    private static ConnectionId SignedIn(TestServer server, string token = "dev:ann")
    {
        ConnectionId connection = server.Connect();
        server.SignIn(connection, token);
        server.TickUntil(() => server.SessionOf(connection).Characters != null);
        server.Transport.ClearSent();
        return connection;
    }

    private static CharacterList LastList(TestServer server, ConnectionId connection)
    {
        InMemoryServerTransport.SentMessage sent = server.Transport.ControlSentTo(connection)
            .Last(message => message.Opcode == MessageOpcode.CharacterList);
        CharacterList.TryRead(sent.Payload, out CharacterList? list);
        return list!;
    }

    private static CreateCharacterResult LastResult(TestServer server, ConnectionId connection)
    {
        InMemoryServerTransport.SentMessage sent = server.Transport.ControlSentTo(connection)
            .Last(message => message.Opcode == MessageOpcode.CreateCharacterResult);
        CreateCharacterResult.TryRead(sent.Payload, out CreateCharacterResult result);
        return result;
    }

    private static CreateCharacterResult Create(TestServer server, ConnectionId connection, string name)
    {
        server.SendCreateCharacter(connection, name);
        server.TickUntil(() => server.Transport.ControlOpcodesSentTo(connection)
            .Contains(MessageOpcode.CreateCharacterResult));
        CreateCharacterResult result = LastResult(server, connection);
        server.Transport.ClearSent();
        return result;
    }

    [TestCase("Abc")]
    [TestCase("Abcdefghijklmnopqrstuv!")]
    [TestCase("Bad Name")]
    [TestCase("Bad_Name")]
    [TestCase("Ännchen")]
    [TestCase("")]
    public void CreateCharacter_WithANameOutsideThePolicy_IsRefusedWithoutTheDatabase(string name)
    {
        var server = new TestServer();
        ConnectionId connection = SignedIn(server);
        server.RunsPersistence = false;

        server.SendCreateCharacter(connection, name);
        server.Tick();

        Assert.That(LastResult(server, connection).Outcome, Is.EqualTo(CreateCharacterOutcome.NameInvalid));
        Assert.That(LastList(server, connection).Characters, Is.Empty);
        Assert.That(server.Persistence.PendingJobs, Is.Zero);
    }

    [TestCase("Abcd")]
    [TestCase("Abcdefghijklmnopqrstuvw")]
    [TestCase("ZZ99zz")]
    public void CreateCharacter_WithANameAtThePolicyBounds_IsCreated(string name)
    {
        var server = new TestServer();

        Assert.That(Create(server, SignedIn(server), name).Outcome, Is.EqualTo(CreateCharacterOutcome.Created));
    }

    [TestCase("Abcd", true)]
    [TestCase("abc1", true)]
    [TestCase("Abc", false)]
    [TestCase("Abcdefghijklmnopqrstuvw", true)]
    [TestCase("Abcdefghijklmnopqrstuvwx", false)]
    [TestCase("Ab cd", false)]
    [TestCase("Ab-cd", false)]
    public void CharacterNamePolicy_AcceptsFourToTwentyThreeAsciiLettersAndDigits(string name, bool isValid)
    {
        Assert.That(CharacterNamePolicy.IsValid(name), Is.EqualTo(isValid));
    }

    [Test]
    public void CreateCharacter_AfterTheDatabaseWasFoundUnreachable_AnswersAtOnce()
    {
        var server = new TestServer();
        ConnectionId connection = SignedIn(server);
        server.Store.IsUnavailable = true;
        server.Persistence.Probe();

        server.SendCreateCharacter(connection, "Ann0");
        server.Tick();

        Assert.That(LastResult(server, connection).Outcome, Is.EqualTo(CreateCharacterOutcome.ServiceUnavailable));
    }

    [Test]
    public void CreateCharacter_BeyondThree_IsRefusedAsLimitReachedWithoutTheDatabase()
    {
        var server = new TestServer();
        ConnectionId connection = SignedIn(server);
        Create(server, connection, "First");
        Create(server, connection, "Second");
        Create(server, connection, "Third");
        server.RunsPersistence = false;

        server.SendCreateCharacter(connection, "Fourth");
        server.Tick();

        Assert.That(LastResult(server, connection).Outcome, Is.EqualTo(CreateCharacterOutcome.LimitReached));
        Assert.That(LastList(server, connection).Characters.Select(entry => entry.Name),
            Is.EqualTo(new[] { "First", "Second", "Third" }));
        Assert.That(server.Persistence.PendingJobs, Is.Zero);
    }

    [Test]
    public void CreateCharacter_FromTheWorldOrBeforeTheList_IsIgnored()
    {
        var server = new TestServer();
        ConnectionId inWorld = server.EnterWorld(7);
        server.Transport.ClearSent();
        ConnectionId early = server.Connect();
        server.SendHello(early);
        server.SendCreateCharacter(early, "Early");
        server.SendCreateCharacter(inWorld, "Late0");

        server.Tick();

        Assert.That(server.SessionManager.IgnoredEvents, Is.EqualTo(2));
        IEnumerable<MessageOpcode> sent = server.Transport.ControlOpcodesSentTo(inWorld)
            .Concat(server.Transport.ControlOpcodesSentTo(early));
        Assert.That(sent, Has.None.EqualTo(MessageOpcode.CreateCharacterResult));
    }

    [Test]
    public void CreateCharacter_StartsWithTheStartingJobFullHealthAtTheMapSpawn()
    {
        var server = new TestServer();
        ConnectionId connection = SignedIn(server);

        CreateCharacterResult result = Create(server, connection, "Ann0");

        NewCharacter created = server.Store.CreatedCharacter(result.Character.Value);
        JobDefinition job = server.Content.Jobs[new JobDefinitionId("job.adventurer")];
        MapDefinition map = server.Content.Maps[job.StartingMap];
        Assert.That(created.Job, Is.EqualTo(job.Id));
        Assert.That(created.Stats, Is.EqualTo(job.StartingStats));
        Assert.That(created.Map, Is.EqualTo(map.Id));
        Assert.That(created.Position, Is.EqualTo(map.SpawnPosition));
        Assert.That(created.Health, Is.EqualTo(71), "level 1 adventurer maximum HP");
        Assert.That(created.CreatedAt, Is.EqualTo(server.Time.UtcNow.UtcDateTime));
    }

    [Test]
    public void CreateCharacter_WhileAnotherIsWaitingForTheDatabase_IsIgnored()
    {
        var server = new TestServer();
        ConnectionId connection = SignedIn(server);
        server.RunsPersistence = false;
        server.SendCreateCharacter(connection, "Ann0");
        server.Tick();

        server.SendCreateCharacter(connection, "Bob0");
        server.Tick();
        server.RunsPersistence = true;
        server.Tick(2);

        Assert.That(server.SessionManager.IgnoredEvents, Is.EqualTo(1));
        Assert.That(LastList(server, connection).Characters.Select(entry => entry.Name), Is.EqualTo(new[] { "Ann0" }));
    }

    [Test]
    public void CreateCharacter_WhileTheDatabaseIsUnreachable_AnswersServiceUnavailableAndKeepsTheConnection()
    {
        var server = new TestServer();
        ConnectionId connection = SignedIn(server);
        server.Store.IsUnavailable = true;

        CreateCharacterResult result = Create(server, connection, "Ann0");

        Assert.That(result.Outcome, Is.EqualTo(CreateCharacterOutcome.ServiceUnavailable));
        Assert.That(server.Transport.Disconnects, Is.Empty);
        Assert.That(server.SessionOf(connection).State, Is.EqualTo(SessionState.Authenticated));
    }

    [Test]
    public void CreateCharacter_WithANameAnotherAccountHasInAnotherCase_IsTaken()
    {
        var server = new TestServer();
        Create(server, SignedIn(server), "Ann0");
        ConnectionId other = SignedIn(server, "dev:bob");

        CreateCharacterResult result = Create(server, other, "ANN0");

        Assert.That(result.Outcome, Is.EqualTo(CreateCharacterOutcome.NameTaken));
        Assert.That(result.Character, Is.EqualTo(default(CharacterId)));
    }

    [Test]
    public void CreateCharacter_WithAValidName_AnswersCreatedAndListsIt()
    {
        var server = new TestServer();
        ConnectionId connection = SignedIn(server);

        server.SendCreateCharacter(connection, "Ann0");
        server.TickUntil(() => server.Transport.ControlOpcodesSentTo(connection).Count == 2);

        Assert.That(
            server.Transport.ControlOpcodesSentTo(connection),
            Is.EqualTo(new[] { MessageOpcode.CreateCharacterResult, MessageOpcode.CharacterList }));
        CreateCharacterResult result = LastResult(server, connection);
        Assert.That(result.Outcome, Is.EqualTo(CreateCharacterOutcome.Created));
        CharacterListEntry listed = LastList(server, connection).Characters.Single();
        Assert.That(listed.Character, Is.EqualTo(result.Character));
        Assert.That(listed.Name, Is.EqualTo("Ann0"));
        Assert.That(listed.Job.Value, Is.EqualTo("job.adventurer"));
        Assert.That(listed.BaseLevel, Is.EqualTo(1));
    }

    [Test]
    public void EnterWorld_ForAnOwnedCharacter_EntersIt()
    {
        var server = new TestServer();
        ConnectionId connection = SignedIn(server);
        CreateCharacterResult created = Create(server, connection, "Ann0");

        server.SendEnterWorld(connection, created.Character.Value);
        server.Tick();

        Assert.That(server.PlayerOf(connection).Character, Is.EqualTo(created.Character));
        Assert.That(server.Transport.ControlOpcodesSentTo(connection).First(), Is.EqualTo(MessageOpcode.WorldEntered));
    }

    [Test]
    public void EnterWorld_ForAnotherAccountsCharacter_IsRefusedLikeAMissingOne()
    {
        var server = new TestServer();
        CreateCharacterResult others = Create(server, SignedIn(server, "dev:bob"), "Bob0");
        ConnectionId thief = SignedIn(server);
        ConnectionId lost = SignedIn(server, "dev:cat");

        server.SendEnterWorld(thief, others.Character.Value);
        server.SendEnterWorld(lost, 999);
        server.Tick(2);

        foreach (ConnectionId connection in new[] { thief, lost })
        {
            Assert.That(server.Transport.ControlSentTo(connection), Is.Empty);
            Assert.That(server.SessionOf(connection).State, Is.EqualTo(SessionState.Authenticated));
        }

        Assert.That(server.SessionManager.IgnoredEvents, Is.EqualTo(2));
        Assert.That(server.World.Maps.Single().Players, Is.Empty);
    }

    [Test]
    public void SignIn_ToANewAccount_ListsNoCharactersAfterServerHello()
    {
        var server = new TestServer();
        ConnectionId connection = server.Connect();

        server.SignIn(connection);
        server.Tick();

        Assert.That(
            server.Transport.ControlOpcodesSentTo(connection),
            Is.EqualTo(new[] { MessageOpcode.ServerHello, MessageOpcode.CharacterList }));
        Assert.That(LastList(server, connection).Characters, Is.Empty);
    }
}
}
