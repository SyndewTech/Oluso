# Entity Framework Migrations

## Quick Start with Script

Use the migration helper script for easy migration management:

```bash
# Show help
./scripts/migrations.sh help

# Add migration to OlusoDbContext (default)
./scripts/migrations.sh add Sqlite AddNewFeature

# Add migration to SCIM context
./scripts/migrations.sh add scim Sqlite Initial

# List migrations
./scripts/migrations.sh list Sqlite
./scripts/migrations.sh list scim Sqlite

# Add migration for all providers (SQLite, SQL Server, PostgreSQL)
./scripts/migrations.sh add-all oluso AddNewFeature

# Add migration for all public contexts (SQLite only)
./scripts/migrations.sh add-all-contexts Initial
```

---

## Apply Migrations (in code)

```csharp
var app = builder.Build();
await app.MigrateOlusoDatabaseAsync();  // Discovers and applies all registered DbContext migrations
app.Run();
```

This automatically discovers all `IMigratableDbContext` implementations and applies their migrations in order.

---

## Available Contexts

### Public Contexts

| Context | Plugin | MigrationOrder | Migration History Table |
|---------|--------|----------------|------------------------|
| `OlusoDbContext` | Core | 0 | `__EFMigrationsHistory` |
| `ScimDbContext` | SCIM | 50 | `__EFMigrationsHistory_Scim` |

---

## Manual Commands

### Generate Migrations

```bash
# OlusoDbContext (SQLite)
dotnet ef migrations add <MigrationName> --context OlusoDbContext \
  --project src/backend/Oluso.EntityFramework \
  --startup-project samples/Oluso.Sample \
  --output-dir Migrations/Sqlite -- --provider Sqlite

# ScimDbContext
dotnet ef migrations add <MigrationName> --context ScimDbContext \
  --project src/backend/Oluso.Enterprise/Scim \
  --startup-project samples/Oluso.Sample \
  --output-dir Migrations/Sqlite -- --provider Sqlite
```

### List Pending Migrations

```bash
dotnet ef migrations list --context OlusoDbContext \
  --project src/backend/Oluso.EntityFramework \
  --startup-project samples/Oluso.Sample -- --provider Sqlite
```

### Remove Last Migration

```bash
dotnet ef migrations remove --context OlusoDbContext \
  --project src/backend/Oluso.EntityFramework \
  --startup-project samples/Oluso.Sample -- --provider Sqlite
```

### Generate SQL Scripts (for deployment)

```bash
# Generate idempotent script
dotnet ef migrations script --idempotent --context OlusoDbContext \
  --project src/backend/Oluso.EntityFramework \
  --startup-project samples/Oluso.Sample \
  --output migrations-oluso-sqlite.sql -- --provider Sqlite
```

---

## Creating a Plugin with Migrations

### 1. Inherit from PluginDbContextBase

```csharp
public class MyPluginDbContext : PluginDbContextBase<MyPluginDbContext>
{
    public const string PluginIdentifier = "MyPlugin";

    protected override string PluginName => PluginIdentifier;

    // Optional: Override migration order (default is 100)
    public override int MigrationOrder => 150;

    public MyPluginDbContext(DbContextOptions<MyPluginDbContext> options)
        : base(options) { }

    public DbSet<MyEntity> MyEntities => Set<MyEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Configure entities...
    }
}
```

### 2. Create Design-Time Factory

```csharp
public class MyPluginDbContextDesignTimeFactory : IDesignTimeDbContextFactory<MyPluginDbContext>
{
    public MyPluginDbContext CreateDbContext(string[] args)
    {
        var provider = GetProvider(args);
        var optionsBuilder = new DbContextOptionsBuilder<MyPluginDbContext>();
        var migrationsTable = PluginDbContextExtensions.GetMigrationsTableName(MyPluginDbContext.PluginIdentifier);

        switch (provider.ToLowerInvariant())
        {
            case "sqlserver":
                optionsBuilder.UseSqlServer("Server=.;Database=MyPluginMigrations;...",
                    x => x.MigrationsHistoryTable(migrationsTable));
                break;
            case "postgres":
                var pgTable = PluginDbContextExtensions.GetMigrationsTableNamePostgres(MyPluginDbContext.PluginIdentifier);
                optionsBuilder.UseNpgsql("Host=localhost;Database=MyPluginMigrations;...",
                    x => x.MigrationsHistoryTable(pgTable));
                break;
            default:
                optionsBuilder.UseSqlite("Data Source=MyPluginMigrations.db",
                    x => x.MigrationsHistoryTable(migrationsTable));
                break;
        }
        return new MyPluginDbContext(optionsBuilder.Options);
    }

    private static string GetProvider(string[] args) { /* ... */ }
}
```

### 3. Register for Migration Discovery

In your DI extension method:

```csharp
public static IServiceCollection AddMyPlugin(this IServiceCollection services, string connectionString)
{
    services.AddDbContext<MyPluginDbContext>(options =>
        options.UseSqlite(connectionString, o =>
            o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(MyPluginDbContext.PluginIdentifier))));

    // Register for automatic migration discovery
    services.AddScoped<IMigratableDbContext>(sp => sp.GetRequiredService<MyPluginDbContext>());

    return services;
}
```

### 4. Generate Initial Migration

```bash
dotnet ef migrations add Initial --context MyPluginDbContext \
  --project path/to/MyPlugin \
  --startup-project path/to/Sample \
  --output-dir Migrations/Sqlite -- --provider Sqlite
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
using Oluso.Core.Data;
using Oluso.EntityFramework;

[DataMigration(typeof(OlusoDbContext))]  // Target context
public class PopulateUserDisplayName : DataMigrationBase
{
    // Unique ID - convention: match schema migration or use descriptive name
    public override string MigrationId => "20240115_PopulateUserDisplayName";

    // Run after this schema migration (null = run after all)
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

### Data Migration Examples

```csharp
// Load data from JSON file
public class LoadCountryCodes : DataMigrationBase
{
    public override async Task UpAsync(DbContext context, IServiceProvider services, CancellationToken ct)
    {
        var env = services.GetService<IHostEnvironment>();
        var path = Path.Combine(env!.ContentRootPath, "Data", "countries.json");
        var countries = JsonSerializer.Deserialize<List<Country>>(await File.ReadAllTextAsync(path, ct));

        foreach (var country in countries!)
        {
            await ExecuteSqlAsync(context,
                $"INSERT INTO Countries (Code, Name) VALUES ({country.Code}, {country.Name})", ct);
        }
    }
}

// Normalize comma-separated values to junction table
public class NormalizeTags : DataMigrationBase
{
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

### How Data Migrations Work

1. **Tracked in separate table**: `__DataMigrationHistory` (or `__DataMigrationHistory_{Plugin}`)
2. **Run exactly once**: Each `MigrationId` is recorded after successful execution
3. **Order of execution**: Sorted by `AfterSchemaMigration`, then by `Order`
4. **Auto-discovered**: Classes implementing `IDataMigration` with `[DataMigration]` attribute
5. **Full service access**: Receive `IServiceProvider` for file access, configuration, etc.

---

## Migration Pipeline

When you call `MigrateOlusoDatabaseAsync()`, the following happens in order:

1. **Schema Migrations** - EF Core migrations (column adds, table creates, etc.)
2. **Data Migrations** - `IDataMigration` implementations tied to schema migrations
3. **Seed Data** - `ISeedableDbContext.SeedAsync()` for idempotent seeding

```
┌─────────────────────────────────────────────────────────────────┐
│                   MigrateOlusoDatabaseAsync()                   │
├─────────────────────────────────────────────────────────────────┤
│  1. Schema Migrations (EF Core)                                │
│     - OlusoDbContext (Order: 0)                                │
│     - ScimDbContext (Order: 50)                                │
│     - Other plugins (Order: 100+)                              │
├─────────────────────────────────────────────────────────────────┤
│  2. Data Migrations (IDataMigration)                           │
│     - Runs once per MigrationId                                │
│     - Full C# logic, file access, DI                           │
│     - Tracked in __DataMigrationHistory                        │
├─────────────────────────────────────────────────────────────────┤
│  3. Seed Data (ISeedableDbContext)                             │
│     - Runs every startup                                       │
│     - Must be idempotent                                       │
│     - Good for dev/test data                                   │
└─────────────────────────────────────────────────────────────────┘
```

---

## How It Works

1. **`IMigratableDbContext`** - Interface implemented by all migratable DbContexts
2. **`PluginDbContextBase<T>`** - Base class providing tenant filtering and migration support
3. **`IDataMigration`** - Interface for data migrations with `UpAsync`/`DownAsync`
4. **`ISeedableDbContext`** - Interface for idempotent seeding after migrations
5. **`MigrateOlusoDatabaseAsync()`** - Extension method that orchestrates the pipeline

### Provider Selection

Design-time factories read the provider from:
1. Command line: `-- --provider Sqlite`
2. Environment variable: `OLUSO_DB_PROVIDER=Sqlite`

Supported values: `Sqlite`, `SqlServer`, `Postgres`
