using System;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     A row's cards as its instance data holds them (Persistence §4): the one strict reader and writer of the stores.
/// </summary>
[TestFixture]
public sealed class CardsJsonTests
{
    private const string Keen = "item.card.forest_crawler";
    private const string Bane = "item.card.gloom_wisp";

    [TestCase("[\"item.card.a\"]")]
    [TestCase("{}")]
    [TestCase("{\"cards\":[]}")]
    [TestCase("{\"cards\":[\"a\",\"b\",\"c\",\"d\",\"e\"]}")]
    [TestCase("{\"cards\":[1]}")]
    [TestCase("{\"cards\":\"item.card.a\"}")]
    [TestCase("{\"cards\":[\"item.card.a\"],\"note\":\"kept\"}")]
    public void Read_OfAnythingButOneToFourCards_IsACorruptRow(string json)
    {
        Action read = () => CardsJson.Read(json);

        Assert.That(read, Throws.InvalidOperationException);
    }

    [Test]
    public void Read_OfNull_IsNoCard()
    {
        Assert.That(CardsJson.Read(null), Is.Empty);
    }

    [Test]
    public void Read_OfTheDatabasesForm_GivesTheCardsInSlotOrder()
    {
        Assert.That(CardsJson.Read($"{{\"cards\": [\"{Keen}\", \"{Bane}\", \"{Keen}\"]}}"),
            Is.EqualTo(new[] { Keen, Bane, Keen }));
    }

    [Test]
    public void Write_OfFiveCards_Throws()
    {
        Action write = () => CardsJson.Write(new[] { Keen, Keen, Keen, Keen, Keen });

        Assert.That(write, Throws.ArgumentException);
    }

    [Test]
    public void Write_OfNoCard_IsNull_AndOfCards_ReadsBack()
    {
        string? written = CardsJson.Write(new[] { Keen, Bane });

        Assert.That(CardsJson.Write(Array.Empty<string>()), Is.Null);
        Assert.That(written, Is.EqualTo($"{{\"cards\":[\"{Keen}\",\"{Bane}\"]}}"));
        Assert.That(CardsJson.Read(written), Is.EqualTo(new[] { Keen, Bane }));
    }
}
}
