# Entity Framework Migrations

Oluso keeps one set of EF Core migrations per database provider. Every context has a provider-specific
subclass (`OlusoDbContextSqlite`, `OlusoDbContextSqlServer`, `OlusoDbContextPostgres`, and the same for each
enterprise context), and each subclass owns its own `Migrations/<Provider>` folder and model snapshot. This
follows the EF Core guidance for
[multiple providers](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers).

## Quick Start with Script

`scripts/migrations.sh` is the supported way to add and manage migrations. It wraps `dotnet ef`, picks the
provider-specific context and output folder, and uses `samples/Oluso.Sample` as the startup project. Run it
from the repository root.

```bash
# Show help
./scripts/migrations.sh help

# Add a migration to OlusoDbContext for every provider (SQLite, SQL Server, PostgreSQL)
./scripts/migrations.sh add-all oluso AddNewFeature

# Add a migration to the SCIM context for every provider
./scripts/migrations.sh add-all scim AddNewFeature

# Add a migration for a single provider
./scripts/migrations.sh add Sqlite AddNewFeature          # OlusoDbContextSqlite
./scripts/migrations.sh add scim SqlServer AddNewFeature  # ScimDbContextSqlServer

# List migrations
./scripts/migrations.sh list Sqlite
./scripts/migrations.sh list scim Sqlite

# Remove the last migration (one provider)
./scripts/migrations.sh remove oluso Sqlite

# Generate an idempotent SQL script, written to migrations-<context>-<provider>.sql
./scripts/migrations.sh script oluso Postgres

# Add a migration to every public context for one provider
./scripts/migrations.sh add-all-contexts Sqlite AddNewFeature
```

Contexts: `oluso` (default), `scim`, `ldap`, `saml`, `fido2`, `verification` (alias `kyb`).
Providers: `Sqlite` (default), `SqlServer`, `Postgres`.

Schema changes should be added with `add-all` so all three providers stay in step.

---

## Apply Migrations (on startup)

Migrations are applied by the host when it starts. The sample host calls `MigrateOlusoDatabaseAsync()` right
after building the app, so pending migrations run on every start:

```csharp
var app = builder.Build();
await app.MigrateOlusoDatabaseAsync();  // Schema migrations, data migrations, then seeding
app.Run();
```

It resolves every registered `IMigratableDbContext`, orders them by `MigrationOrder` and applies their pending
migrations. Contexts that use a non-relational provider (for example the EF InMemory provider in tests) are
created with `EnsureCreated` instead. If a migration fails, the error is logged and rethrown, so the host does
not start against a half-migrated database.

There is no separate `dotnet ef database update` step in normal use.

---

## Available Contexts

| Context | Module | MigrationOrder | Migration History Table |
|---------|--------|----------------|------------------------|
| `OlusoDbContext` | Core | 0 | `__EFMigrationsHistory` |
| `ScimDbContext` | SCIM | 50 | `__EFMigrationsHistory_Scim` |
| `LdapDbContext` | LDAP | 60 | `__EFMigrationsHistory_Ldap` |
| `SamlDbContext` | SAML | 70 | `__EFMigrationsHistory_Saml` |
| `VerificationDbContext` | Business verification (KYB) | 70 | `__EFMigrationsHistory_Verification` |
| `Fido2DbContext` | FIDO2/WebAuthn | 80 | `__EFMigrationsHistory_Fido2` |

On PostgreSQL the module history tables are lowercase (`__efmigrationshistory_scim` and so on).

Each module context is registered by its `Add*ForProvider(provider, connectionString)` extension
(`AddScimForProvider`, `AddSamlForProvider`, `AddFido2ForProvider`, `AddOlusoVerificationForProvider`,
`AddLdapForProvider`), which registers the provider-specific context as `IMigratableDbContext`. A context that is
not registered is not migrated.

---

## Manual Commands

The script runs these commands. Use them directly only when you need options the script does not expose.
Always name the provider-specific context.

### Generate Migrations

```bash
# OlusoDbContext (SQLite)
dotnet ef migrations add <MigrationName> --context OlusoDbContextSqlite \
  --project src/backend/Oluso.EntityFramework \
  --startup-project samples/Oluso.Sample \
  --output-dir Migrations/Sqlite

# ScimDbContext (PostgreSQL)
dotnet ef migrations add <MigrationName> --context ScimDbContextPostgres \
  --project src/backend/Oluso.Enterprise/Scim \
  --startup-project samples/Oluso.Sample \
  --output-dir Migrations/Postgres
```

### List Migrations

```bash
dotnet ef migrations list --context OlusoDbContextSqlite \
  --project src/backend/Oluso.EntityFramework \
  --startup-project samples/Oluso.Sample
```

### Remove Last Migration

```bash
dotnet ef migrations remove --context OlusoDbContextSqlite \
  --project src/backend/Oluso.EntityFramework \
  --startup-project samples/Oluso.Sample
```

### Generate SQL Scripts (for deployment)

```bash
# Generate idempotent script
dotnet ef migrations script --idempotent --context OlusoDbContextSqlServer \
  --project src/backend/Oluso.EntityFramework \
  --startup-project samples/Oluso.Sample \
  --output migrations-oluso-sqlserver.sql
```

---

## Creating a Plugin with Migrations

### 1. Inherit from PluginDbContextBase

`PluginDbContextBase<T>` (in `Oluso.Core.Data`) adds tenant query filters, sets `TenantId` on save, validates
tenant-scoped foreign keys and gives the plugin its own migration history table. `OnModelCreating` is sealed:
put entity configuration in `ConfigureModel`.

```csharp
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Data;

public class MyPluginDbContext : PluginDbContextBase<MyPluginDbContext>
{
    public const string PluginIdentifier = "MyPlugin";

    protected override string PluginName => PluginIdentifier;

    // SQLite: tables become "MyPlugin_<Table>"; SQL Server and PostgreSQL: schema "MyPlugin" / "myplugin".
    // Return "" only when table names must follow an external spec.
    protected override string? TablePrefix => "MyPlugin_";

    // Optional: Override migration order (default is 100)
    public override int MigrationOrder => 150;

    public MyPluginDbContext(DbContextOptions<MyPluginDbContext> options)
        : base(options) { }

    // Used by the provider-specific subclasses below
    protected MyPluginDbContext(DbContextOptions options)
        : base(options) { }

    public DbSet<MyEntity> MyEntities => Set<MyEntity>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        // Configure entities...
    }
}
```

### 2. Add Provider-Specific Contexts

Each provider gets a subclass with a parameterless constructor for the EF tools and a runtime constructor for
DI. The migrations for that provider are generated against that subclass.

```csharp
public class MyPluginDbContextSqlite : MyPluginDbContext
{
    public MyPluginDbContextSqlite() : base(CreateDesignTimeOptions()) { }

    public MyPluginDbContextSqlite(DbContextOptions<MyPluginDbContextSqlite> options)
        : base(options) { }

    private static DbContextOptions<MyPluginDbContext> CreateDesignTimeOptions() =>
        new DbContextOptionsBuilder<MyPluginDbContext>()
            .UseSqlite("Data Source=:memory:", o => o.MigrationsHistoryTable(
                PluginDbContextExtensions.GetMigrationsTableName(PluginIdentifier)))
            .Options;
}

public class MyPluginDbContextPostgres : MyPluginDbContext
{
    public MyPluginDbContextPostgres() : base(CreateDesignTimeOptions()) { }

    public MyPluginDbContextPostgres(DbContextOptions<MyPluginDbContextPostgres> options)
        : base(options) { }

    private static DbContextOptions<MyPluginDbContext> CreateDesignTimeOptions() =>
        new DbContextOptionsBuilder<MyPluginDbContext>()
            .UseNpgsql("Host=localhost;Database=MyPlugin;Username=postgres;Password=postgres",
                o => o.MigrationsHistoryTable(
                    PluginDbContextExtensions.GetMigrationsTableNamePostgres(PluginIdentifier)))
            .Options;
}

// MyPluginDbContextSqlServer follows the same pattern with UseSqlServer and GetMigrationsTableName.
```

### 3. Register for Migration Discovery

In your DI extension method, register the provider-specific context and expose it as the base type and as
`IMigratableDbContext`:

```csharp
public static IServiceCollection AddMyPluginSqlite(this IServiceCollection services, string connectionString)
{
    services.AddDbContext<MyPluginDbContextSqlite>(options =>
        options.UseSqlite(connectionString, o => o.MigrationsHistoryTable(
            PluginDbContextExtensions.GetMigrationsTableName(MyPluginDbContext.PluginIdentifier))));

    services.AddScoped<MyPluginDbContext>(sp => sp.GetRequiredService<MyPluginDbContextSqlite>());
    services.AddScoped<IMigratableDbContext>(sp => sp.GetRequiredService<MyPluginDbContextSqlite>());

    return services;
}
```

If the context also implements `ISeedableDbContext`, register it under that interface too. The enterprise
modules add a `Add*ForProvider(provider, connectionString)` method that switches between the three; follow that
pattern so hosts can pass `Oluso:Database:Provider` straight through.

### 4. Generate Initial Migration

`scripts/migrations.sh` only knows the built-in contexts, so a plugin outside this repository runs `dotnet ef`
directly, once per provider:

```bash
dotnet ef migrations add Initial --context MyPluginDbContextSqlite \
  --project path/to/MyPlugin \
  --startup-project path/to/Host \
  --output-dir Migrations/Sqlite
```

---

## Data Migrations (Complex Operations)

For operations beyond simple schema changes (populating new columns, splitting tables, loading seed data from files), use **Data Migrations**.

### When to Use Data Migrations

- Populating a new column with computed data from existing columns
- Splitting/merging tables with data transformation
- Loading lookup table data from JSON/CSV files
- Creating many-to-many junction table data from denormalized columns
- Any migration requiring C# logic, DI services, or file access

### Creating a Data Migration

```csharp
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Data;
using Oluso.EntityFramework;

[DataMigration(typeof(OlusoDbContext))]  // Target context (provider-specific subclasses match too)
public class PopulateUserDisplayName : DataMigrationBase
{
    // Unique ID - convention: match schema migration or use descriptive name
    public override string MigrationId => "20240115_PopulateUserDisplayName";

    // Run straight after this schema migration (null = run after all schema migrations)
    public override string? AfterSchemaMigration => "20240115143022_AddUserDisplayName";

    public override string Description => "Populate DisplayName from FirstName + LastName";

    public override async Task UpAsync(DbContext context, IServiceProvider services, CancellationToken ct)
    {
        // Use raw SQL for bulk operations (faster)
        await ExecuteSqlRawAsync(context, @"
            UPDATE Users
            SET DisplayName = COALESCE(FirstName, '') || ' ' || COALESCE(LastName, '')
            WHERE DisplayName IS NULL
        ", ct);
    }

    public override async Task DownAsync(DbContext context, IServiceProvider services, CancellationToken ct)
    {
        await ExecuteSqlRawAsync(context, "UPDATE Users SET DisplayName = NULL", ct);
    }
}
```

Instead of `AfterSchemaMigration` you can set `RunAt` to a migration timestamp (for example `"20240115000000"`):
the data migration then runs after the first schema migration whose timestamp is at or past it. `Order` breaks
ties between data migrations at the same point. A data migration needs a public parameterless constructor.

### Data Migration Examples

```csharp
// Load data from JSON file
[DataMigration(typeof(MyPluginDbContext))]
public class LoadCountryCodes : DataMigrationBase
{
    public override string MigrationId => "LoadCountryCodes";
    public override string Description => "Load country codes from Data/countries.json";

    public override async Task UpAsync(DbContext context, IServiceProvider services, CancellationToken ct)
    {
        var env = services.GetRequiredService<IHostEnvironment>();
        var path = Path.Combine(env.ContentRootPath, "Data", "countries.json");
        var countries = JsonSerializer.Deserialize<List<Country>>(await File.ReadAllTextAsync(path, ct));

        foreach (var country in countries!)
        {
            await ExecuteSqlAsync(context,
                $"INSERT INTO Countries (Code, Name) VALUES ({country.Code}, {country.Name})", ct);
        }
    }
}

// Normalize comma-separated values to junction table (SQL Server syntax)
[DataMigration(typeof(MyPluginDbContext))]
public class NormalizeTags : DataMigrationBase
{
    public override string MigrationId => "NormalizeTags";
    public override string Description => "Move Articles.Tags into ArticleTags";

    public override async Task UpAsync(DbContext context, IServiceProvider services, CancellationToken ct)
    {
        await ExecuteSqlRawAsync(context, @"
            INSERT INTO ArticleTags (ArticleId, TagId)
            SELECT a.Id, t.Id
            FROM Articles a
            CROSS APPLY STRING_SPLIT(a.Tags, ',') AS split
            JOIN Tags t ON t.Name = TRIM(split.value)
        ", ct);
    }
}
```

`src/backend/Oluso.EntityFramework/DataMigrations` holds the core data migrations, which are worked examples
of the rules below.

### How Data Migrations Work

1. **Tracked in separate table**: `__DataMigrationHistory` for `OlusoDbContext`, `__DataMigrationHistory_{Plugin}` for plugin contexts
2. **Run exactly once**: Each `MigrationId` is recorded after successful execution
3. **Interleaved with schema migrations**: schema migrations are applied one at a time, and the data migrations bound to each (`AfterSchemaMigration` or `RunAt`) run straight after it
4. **Auto-discovered**: Non-abstract classes implementing `IDataMigration` with a `[DataMigration]` attribute, in the context's assembly, the entry assembly, or any loaded assembly that references the context's assembly
5. **Full service access**: Receive `IServiceProvider` for file access, configuration, etc.

### Data Migrations Bound to a Schema Migration Don't Load Entities

A data migration with `AfterSchemaMigration` (or `RunAt`) runs against the schema as it stood after that
migration, but its code is compiled against today's entity model. On a new database, or one upgraded across
several releases, the later schema migrations haven't run yet when it does. Loading a whole entity
(`ToListAsync`, `FirstOrDefaultAsync`, `FindAsync`, `Include`, or a store or service that does) selects every
column the model maps today, and `SaveChanges` writes them, so once a later migration adds a column to that
table the data migration fails with "no such column" / "invalid column name". Production never sees it (the
data migration already ran there), but new tenant databases, test environments and CI do.

So a bound data migration:

- **Reads projections** of the columns that exist at its anchor: `.Select(x => new { x.Id, x.Status })`,
  or `SqlQueryRaw` naming the columns.
- **Writes with `ExecuteUpdateAsync`** setting only those columns (`.Where(x => x.Id == id).ExecuteUpdateAsync(...)`),
  or with raw SQL that names them (inserts included). Never `Add`/`Update`/`Attach` + `SaveChanges`.
- Wraps its writes in a transaction when it makes more than one, so a failure leaves nothing half done:
  `db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync(ct) : null`.
- Resolves table and column names with `DataMigrationSqlHelpers` (`Oluso.EntityFramework.DataMigrations`:
  `QualifiedTable<T>`, `Column<T>`, `Q`) so provider naming (SQLite prefixes, SQL Server / PostgreSQL schemas
  and quoting) is honoured.

A data migration with neither `AfterSchemaMigration` nor `RunAt` runs after every schema migration, against
the schema the model describes, and may use the model freely.

When the change needs a column that a later step tightens or removes, split it:

1. Schema migration **A** adds the new column, nullable (or with a default).
2. A data migration with `AfterSchemaMigration = "A"` fills it, reading and writing as above.
3. Schema migration **B** makes it required, adds the unique index, or drops the old column.

`AddUserRoleTenantId` → `BackfillUserRoleTenantId` → `RequireUserRoleTenantId` follows this pattern.

`tests/Oluso.Tests/DataMigrations/FreshDatabaseMigrationTests.cs` builds a new SQLite database the way a
deploy does (`MigrateSchemaWithDataMigrationsAsync`, then the remaining data migrations), inserting rows
before the anchors so bound data migrations have something to change. It fails if a bound data migration
loads a whole entity or calls `SaveChanges`, before any later column exists to break it. When you add a bound
data migration, add seed rows there.

---

## Migration Pipeline

When you call `MigrateOlusoDatabaseAsync()`, the following happens in order:

1. **Schema Migrations** - EF Core migrations (column adds, table creates, etc.), applied one at a time per context, with bound data migrations run after each
2. **Remaining Data Migrations** - unbound data migrations, plus any bound ones not yet recorded (for example when their schema migration was applied earlier)
3. **Seed Data** - `ISeedableDbContext.SeedAsync()` for idempotent seeding

```
┌─────────────────────────────────────────────────────────────────┐
│                   MigrateOlusoDatabaseAsync()                   │
├─────────────────────────────────────────────────────────────────┤
│  1. Schema Migrations (EF Core), by MigrationOrder              │
│     - OlusoDbContext (0), ScimDbContext (50), LdapDbContext (60)│
│     - SamlDbContext / VerificationDbContext (70)                │
│     - Fido2DbContext (80), other plugins (100+)                 │
│     - Bound data migrations run after each schema migration     │
├─────────────────────────────────────────────────────────────────┤
│  2. Remaining Data Migrations (IDataMigration)                  │
│     - Runs once per MigrationId                                 │
│     - Full C# logic, file access, DI                            │
│     - Tracked in __DataMigrationHistory[_{Plugin}]              │
├─────────────────────────────────────────────────────────────────┤
│  3. Seed Data (ISeedableDbContext)                              │
│     - Runs every startup                                        │
│     - Must be idempotent                                        │
└─────────────────────────────────────────────────────────────────┘
```

---

## How It Works

1. **`IMigratableDbContext`** - Interface implemented by all migratable DbContexts (`MigrationName`, `MigrationOrder`)
2. **`PluginDbContextBase<T>`** - Base class providing tenant filtering, table naming and migration support
3. **`IDataMigration` / `DataMigrationBase`** - Data migrations with `UpAsync`/`DownAsync`
4. **`ISeedableDbContext`** - Interface for idempotent seeding after migrations
5. **`MigrateOlusoDatabaseAsync()`** - Extension method that orchestrates the pipeline

### Provider Selection

At design time the provider comes from the context type you name: `OlusoDbContextSqlite`,
`OlusoDbContextSqlServer` or `OlusoDbContextPostgres` (and likewise for each module context). Each has a
parameterless constructor configured for its provider, so no connection string or `--provider` argument is
needed to add migrations.

At runtime the host picks the provider. The sample reads `Oluso:Database:Provider` (`Sqlite`, `SqlServer` or
`PostgreSQL`/`Postgres`; default `Sqlite`) and `ConnectionStrings:OlusoDb`, and passes them to
`AddEntityFrameworkStoresForProvider` and the module `Add*ForProvider` methods.
