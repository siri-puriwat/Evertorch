using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server.Tests
{
/// <summary>
///     What one part of an inspection holds in a position, for the tests that read a whole answer in one part.
/// </summary>
internal static class InspectedParts
{
    public static InspectedItem ItemIn(this InspectedEquipment part, EquipmentSlot slot)
    {
        return part.Entries.Single(entry => entry.Slot == slot).Item;
    }
}
}
