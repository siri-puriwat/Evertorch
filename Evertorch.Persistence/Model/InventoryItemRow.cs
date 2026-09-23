namespace Evertorch.Persistence
{
internal sealed class InventoryItemRow
{
    public long Id { get; set; }

    public long CharacterId { get; set; }

    public string ItemDefinitionId { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public int RefineLevel { get; set; }

    public string? InstanceDataJson { get; set; }

    public int Version { get; set; }
}
}
