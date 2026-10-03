namespace Evertorch.Persistence
{
/// <summary>
///     One row of an account's storage: a stack of one item definition.
/// </summary>
internal sealed class StorageItemRow
{
    public long Id { get; set; }

    public long AccountId { get; set; }

    public string ItemDefinitionId { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public int RefineLevel { get; set; }

    public string? InstanceDataJson { get; set; }

    public int Version { get; set; }
}
}
