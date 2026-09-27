using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Evertorch.Persistence
{
/// <summary>
///     The durable schema (Persistence §4). One instance serves one unit of work and is never shared.
/// </summary>
internal sealed class EvertorchDbContext : DbContext
{
    public const int MaxLoginLength = 128;
    public const int MaxNameLength = 23;
    public const int MaxDefinitionIdLength = 64;
    public const int MaxQuantity = 1_000_000;
    public const long MaxInventoryRevision = uint.MaxValue;

    /// <summary>
    ///     The coin cap (Gameplay Systems §11.3).
    /// </summary>
    public const long MaxCurrency = 1_000_000_000;

    private const int MaxStatusLength = 16;
    private const int MaxSchemeLength = 32;
    private const int MaxSlotLength = 16;
    private const int MaxOperationTypeLength = 32;
    private const int MaxQuestStateLength = 16;

    public EvertorchDbContext(DbContextOptions<EvertorchDbContext> options)
        : base(options)
    {
    }

    public DbSet<AccountRow> Accounts => Set<AccountRow>();

    public DbSet<CharacterRow> Characters => Set<CharacterRow>();

    public DbSet<InventoryItemRow> InventoryItems => Set<InventoryItemRow>();

    public DbSet<EquipmentRow> Equipment => Set<EquipmentRow>();

    public DbSet<LedgerRow> Ledger => Set<LedgerRow>();

    public DbSet<CharacterQuestRow> CharacterQuests => Set<CharacterQuestRow>();

    public DbSet<SessionTokenRow> SessionTokens => Set<SessionTokenRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAccounts(modelBuilder.Entity<AccountRow>());
        ConfigureCharacters(modelBuilder.Entity<CharacterRow>());
        ConfigureInventory(modelBuilder.Entity<InventoryItemRow>());
        ConfigureEquipment(modelBuilder.Entity<EquipmentRow>());
        ConfigureLedger(modelBuilder.Entity<LedgerRow>());
        ConfigureQuests(modelBuilder.Entity<CharacterQuestRow>());
        ConfigureSessionTokens(modelBuilder.Entity<SessionTokenRow>());

        foreach (IMutableEntityType entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (IMutableProperty property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static void ConfigureAccounts(EntityTypeBuilder<AccountRow> account)
    {
        account.ToTable("accounts", table =>
        {
            table.HasCheckConstraint(
                "ck_accounts_status",
                $"status IN ('{AccountStatus.Active}', '{AccountStatus.Disabled}')");
            table.HasCheckConstraint(
                "ck_accounts_password",
                "(password_hash IS NULL) = (password_scheme IS NULL)");
        });
        account.HasKey(row => row.Id).HasName("pk_accounts");
        account.Property(row => row.Id).UseIdentityAlwaysColumn();
        account.Property(row => row.LoginNormalized).HasMaxLength(MaxLoginLength).IsRequired();
        account.Property(row => row.PasswordScheme).HasMaxLength(MaxSchemeLength);
        account.Property(row => row.Status).HasMaxLength(MaxStatusLength).IsRequired();
        account.HasIndex(row => row.LoginNormalized).IsUnique().HasDatabaseName("ux_accounts_login_normalized");
    }

    private static void ConfigureCharacters(EntityTypeBuilder<CharacterRow> character)
    {
        character.ToTable("characters", table =>
        {
            table.HasCheckConstraint(
                "ck_characters_name",
                $"name ~ '^[A-Za-z0-9]{{4,{MaxNameLength}}}$' AND name_normalized = lower(name)");
            table.HasCheckConstraint("ck_characters_levels", "base_level >= 1 AND job_level >= 1");
            table.HasCheckConstraint("ck_characters_experience", "base_exp >= 0 AND job_exp >= 0");
            table.HasCheckConstraint(
                "ck_characters_stats",
                "str >= 0 AND agi >= 0 AND vit >= 0 AND int >= 0 AND dex >= 0 AND luk >= 0");
            table.HasCheckConstraint(
                "ck_characters_resources",
                $"hp >= 0 AND sp >= 0 AND currency BETWEEN 0 AND {MaxCurrency}");
            table.HasCheckConstraint(
                "ck_characters_inventory_revision",
                $"inventory_revision BETWEEN 0 AND {MaxInventoryRevision}");
        });
        character.HasKey(row => row.Id).HasName("pk_characters");
        character.Property(row => row.Id).UseIdentityAlwaysColumn();
        character.Property(row => row.Name).HasMaxLength(MaxNameLength).IsRequired();
        character.Property(row => row.NameNormalized).HasMaxLength(MaxNameLength).IsRequired();
        character.Property(row => row.JobDefinitionId).HasMaxLength(MaxDefinitionIdLength).IsRequired();
        character.Property(row => row.MapDefinitionId).HasMaxLength(MaxDefinitionIdLength).IsRequired();
        character.Property(row => row.Version).IsConcurrencyToken();
        character.HasIndex(row => row.NameNormalized).IsUnique().HasDatabaseName("ux_characters_name_normalized");
        character.HasIndex(row => row.AccountId).HasDatabaseName("ix_characters_account_id");
        character.HasOne<AccountRow>()
            .WithMany()
            .HasForeignKey(row => row.AccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_characters_accounts");
    }

    private static void ConfigureInventory(EntityTypeBuilder<InventoryItemRow> item)
    {
        item.ToTable("inventory_items", table =>
        {
            table.HasCheckConstraint("ck_inventory_items_quantity", $"quantity BETWEEN 1 AND {MaxQuantity}");
            table.HasCheckConstraint("ck_inventory_items_refine_level", "refine_level >= 0");
        });
        item.HasKey(row => row.Id).HasName("pk_inventory_items");
        item.Property(row => row.Id).UseIdentityAlwaysColumn();
        item.Property(row => row.ItemDefinitionId).HasMaxLength(MaxDefinitionIdLength).IsRequired();
        item.Property(row => row.InstanceDataJson).HasColumnType("jsonb");
        item.Property(row => row.Version).IsConcurrencyToken();

        // The pair is the target of the equipment's ownership key, so a slot can only hold its own character's item.
        item.HasAlternateKey(row => new { row.CharacterId, row.Id }).HasName("ak_inventory_items_character_id_id");
        item.HasOne<CharacterRow>()
            .WithMany()
            .HasForeignKey(row => row.CharacterId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_inventory_items_characters");
    }

    private static void ConfigureEquipment(EntityTypeBuilder<EquipmentRow> equipment)
    {
        equipment.ToTable("equipment", table =>
        {
            table.HasCheckConstraint(
                "ck_equipment_slot",
                $"slot IN ('{EquipmentRow.WeaponSlot}', '{EquipmentRow.ArmorSlot}')");
        });
        equipment.HasKey(row => new { row.CharacterId, row.Slot }).HasName("pk_equipment");
        equipment.Property(row => row.Slot).HasMaxLength(MaxSlotLength).IsRequired();
        equipment.Property(row => row.Version).IsConcurrencyToken();
        equipment.HasIndex(row => row.InventoryItemId).IsUnique().HasDatabaseName("ux_equipment_inventory_item_id");
        equipment.HasIndex(row => new { row.CharacterId, row.InventoryItemId })
            .HasDatabaseName("ix_equipment_character_id_inventory_item_id");
        equipment.HasOne<CharacterRow>()
            .WithMany()
            .HasForeignKey(row => row.CharacterId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_equipment_characters");
        equipment.HasOne<InventoryItemRow>()
            .WithMany()
            .HasForeignKey(row => new { row.CharacterId, row.InventoryItemId })
            .HasPrincipalKey(row => new { row.CharacterId, row.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_equipment_owned_inventory_item");
    }

    private static void ConfigureLedger(EntityTypeBuilder<LedgerRow> entry)
    {
        entry.ToTable("economy_ledger", table =>
        {
            table.HasCheckConstraint(
                "ck_economy_ledger_operation_type",
                $"operation_type IN ('{LedgerRow.PickupOperation}', '{LedgerRow.EquipOperation}', "
                + $"'{LedgerRow.UnequipOperation}', '{LedgerRow.ConsumeOperation}', '{LedgerRow.BuyOperation}', "
                + $"'{LedgerRow.SellOperation}', '{LedgerRow.QuestRewardOperation}')");
        });
        entry.HasKey(row => row.Id).HasName("pk_economy_ledger");
        entry.Property(row => row.Id).UseIdentityAlwaysColumn();
        entry.Property(row => row.OperationType).HasMaxLength(MaxOperationTypeLength).IsRequired();
        entry.Property(row => row.ItemDefinitionId).HasMaxLength(MaxDefinitionIdLength);
        entry.Property(row => row.MetadataJson).HasColumnType("jsonb");
        entry.HasIndex(row => row.OperationId).IsUnique().HasDatabaseName("ux_economy_ledger_operation_id");
        entry.HasIndex(row => row.ActorCharacterId).HasDatabaseName("ix_economy_ledger_actor_character_id");
        entry.HasOne<CharacterRow>()
            .WithMany()
            .HasForeignKey(row => row.ActorCharacterId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_economy_ledger_characters");
    }

    private static void ConfigureQuests(EntityTypeBuilder<CharacterQuestRow> quest)
    {
        quest.ToTable("character_quests", table =>
        {
            table.HasCheckConstraint(
                "ck_character_quests_state",
                $"state IN ('{CharacterQuestRow.ActiveState}', '{CharacterQuestRow.CompletedState}')");
            table.HasCheckConstraint("ck_character_quests_progress", "progress >= 0");
        });
        quest.HasKey(row => new { row.CharacterId, row.QuestDefinitionId }).HasName("pk_character_quests");
        quest.Property(row => row.QuestDefinitionId).HasMaxLength(MaxDefinitionIdLength).IsRequired();
        quest.Property(row => row.State).HasMaxLength(MaxQuestStateLength).IsRequired();
        quest.Property(row => row.Version).IsConcurrencyToken();
        quest.HasOne<CharacterRow>()
            .WithMany()
            .HasForeignKey(row => row.CharacterId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_character_quests_characters");
    }

    private static void ConfigureSessionTokens(EntityTypeBuilder<SessionTokenRow> token)
    {
        token.ToTable("session_tokens", table =>
        {
            table.HasCheckConstraint(
                "ck_session_tokens_token_hash",
                $"octet_length(token_hash) = {SessionTokenLimits.HashLength}");
            table.HasCheckConstraint("ck_session_tokens_expiry", "expires_at > issued_at");
        });
        token.HasKey(row => row.TokenHash).HasName("pk_session_tokens");
        token.HasIndex(row => row.AccountId).HasDatabaseName("ix_session_tokens_account_id");
        token.HasIndex(row => row.ExpiresAt).HasDatabaseName("ix_session_tokens_expires_at");
        token.HasOne<AccountRow>()
            .WithMany()
            .HasForeignKey(row => row.AccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_session_tokens_accounts");
    }

    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (int index = 0; index < name.Length; index++)
        {
            char character = name[index];
            if (char.IsUpper(character) && index > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}
}
