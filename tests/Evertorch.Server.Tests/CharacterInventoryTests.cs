using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The server's copy of a character's inventory takes each committed row as it now is (Persistence §5).
/// </summary>
[TestFixture]
public sealed class CharacterInventoryTests
{
    private static readonly ItemDefinitionId Gel = new("item.material.slime_gel");

    [Test]
    public void Apply_ChangesARow_AddsANewOne_AndDropsOneTheChangeEmptied()
    {
        var inventory =
            new CharacterInventory(4, new[] { new InventoryEntry(11, Gel, 3), new InventoryEntry(12, Gel, 1) });

        inventory.Apply(5, new InventoryEntry(11, Gel, 5));
        inventory.Apply(6, new InventoryEntry(13, Gel, 2));
        inventory.Apply(7, new InventoryEntry(12, Gel, 0));

        Assert.That(
            inventory.Rows.Select(row => (row.InventoryItem, row.Quantity)),
            Is.EqualTo(new[] { (11L, 5u), (13L, 2u) }));
        Assert.That(inventory.Revision, Is.EqualTo(7u));
    }
}
}
