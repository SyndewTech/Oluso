using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Oluso.Core.Data;

namespace Oluso.EntityFramework.DataMigrations.Examples;

// ============================================================================
// EXAMPLE 1: Populate a new column from existing columns
// ============================================================================

/// <summary>
/// Example: After adding a DisplayName column, populate it from FirstName + LastName.
/// This runs after the schema migration that added the column.
/// </summary>
// [DataMigration(typeof(OlusoDbContext))] // Uncomment to enable
public class PopulateUserDisplayName : DataMigrationBase
{
    public override string MigrationId => "20240115_PopulateUserDisplayName";

    // Run after the schema migration that added the DisplayName column
    public override string? AfterSchemaMigration => "20240115143022_AddUserDisplayName";

    public override string Description => "Populate DisplayName from FirstName and LastName";

    public override async Task UpAsync(DbContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        // Use raw SQL for bulk updates (much faster than loading entities)
        await ExecuteSqlRawAsync(context, @"
            UPDATE Users
            SET DisplayName = COALESCE(FirstName, '') || ' ' || COALESCE(LastName, '')
            WHERE DisplayName IS NULL OR DisplayName = ''
        ", cancellationToken);
    }

    public override async Task DownAsync(DbContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        // Clear the DisplayName column
        await ExecuteSqlRawAsync(context, @"
            UPDATE Users SET DisplayName = NULL
        ", cancellationToken);
    }
}

// ============================================================================
// EXAMPLE 2: Load seed data from a JSON file
// ============================================================================

/// <summary>
/// Example: Load lookup data from a JSON file after adding a new table.
/// </summary>
// [DataMigration(typeof(OlusoDbContext))] // Uncomment to enable
public class LoadCountryCodesFromJson : DataMigrationBase
{
    public override string MigrationId => "20240120_LoadCountryCodes";
    public override string? AfterSchemaMigration => "20240120_AddCountryTable";
    public override string Description => "Load country codes from embedded JSON resource";

    public override async Task UpAsync(DbContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        // Get the host environment to read files
        var env = services.GetService<IHostEnvironment>();
        var basePath = env?.ContentRootPath ?? Directory.GetCurrentDirectory();
        var dataPath = Path.Combine(basePath, "Data", "countries.json");

        if (File.Exists(dataPath))
        {
            var json = await File.ReadAllTextAsync(dataPath, cancellationToken);
            var countries = JsonSerializer.Deserialize<List<CountryDto>>(json);

            if (countries != null)
            {
                foreach (var country in countries)
                {
                    await ExecuteSqlAsync(context,
                        $"INSERT INTO Countries (Code, Name) VALUES ({country.Code}, {country.Name})",
                        cancellationToken);
                }
            }
        }
    }

    private class CountryDto
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
    }
}

// ============================================================================
// EXAMPLE 3: Split a table into multiple tables
// ============================================================================

/// <summary>
/// Example: Split UserSettings out of Users table into a separate table.
/// </summary>
// [DataMigration(typeof(OlusoDbContext))] // Uncomment to enable
public class SplitUserSettings : DataMigrationBase
{
    public override string MigrationId => "20240201_SplitUserSettings";
    public override string? AfterSchemaMigration => "20240201_AddUserSettingsTable";
    public override string Description => "Migrate user settings from Users to UserSettings table";

    public override async Task UpAsync(DbContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        // Copy settings from Users to new UserSettings table
        await ExecuteSqlRawAsync(context, @"
            INSERT INTO UserSettings (UserId, Theme, Language, Timezone, NotificationsEnabled)
            SELECT Id, Theme, Language, Timezone, NotificationsEnabled
            FROM Users
            WHERE Theme IS NOT NULL OR Language IS NOT NULL
        ", cancellationToken);
    }

    public override async Task DownAsync(DbContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        // Copy settings back to Users table
        await ExecuteSqlRawAsync(context, @"
            UPDATE Users
            SET Theme = us.Theme, Language = us.Language, Timezone = us.Timezone
            FROM UserSettings us
            WHERE Users.Id = us.UserId
        ", cancellationToken);
    }
}

// ============================================================================
// EXAMPLE 4: Create many-to-many junction table data
// ============================================================================

/// <summary>
/// Example: After normalizing tags from a comma-separated column to a junction table.
/// </summary>
// [DataMigration(typeof(OlusoDbContext))] // Uncomment to enable
public class NormalizeTags : DataMigrationBase
{
    public override string MigrationId => "20240215_NormalizeTags";
    public override string? AfterSchemaMigration => "20240215_AddArticleTagsTable";
    public override int Order => 10; // Run after other migrations for this schema migration
    public override string Description => "Split comma-separated tags into normalized junction table";

    public override async Task UpAsync(DbContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        // For SQLite - split comma-separated tags
        // This is provider-specific, so we'd need different SQL for each provider
        var providerName = context.Database.ProviderName ?? "";

        if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            // SQLite doesn't have STRING_SPLIT, so we use a recursive CTE
            await ExecuteSqlRawAsync(context, @"
                WITH RECURSIVE split(ArticleId, tag, rest) AS (
                    SELECT Id, '', Tags || ',' FROM Articles WHERE Tags IS NOT NULL
                    UNION ALL
                    SELECT ArticleId,
                           TRIM(SUBSTR(rest, 1, INSTR(rest, ',') - 1)),
                           SUBSTR(rest, INSTR(rest, ',') + 1)
                    FROM split WHERE rest <> ''
                )
                INSERT INTO ArticleTags (ArticleId, TagId)
                SELECT s.ArticleId, t.Id
                FROM split s
                JOIN Tags t ON t.Name = s.tag
                WHERE s.tag <> ''
            ", cancellationToken);
        }
        else if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteSqlRawAsync(context, @"
                INSERT INTO ArticleTags (ArticleId, TagId)
                SELECT a.Id, t.Id
                FROM Articles a
                CROSS APPLY STRING_SPLIT(a.Tags, ',') AS split
                JOIN Tags t ON t.Name = TRIM(split.value)
                WHERE a.Tags IS NOT NULL
            ", cancellationToken);
        }
    }
}

// ============================================================================
// EXAMPLE 5: Data migration with complex C# logic
// ============================================================================

/// <summary>
/// Example: Hash passwords that were stored in plain text (security fix).
/// </summary>
// [DataMigration(typeof(OlusoDbContext))] // Uncomment to enable
public class HashPlainTextPasswords : DataMigrationBase
{
    public override string MigrationId => "20240301_HashPlainTextPasswords";
    public override string? AfterSchemaMigration => null; // Run after all schema migrations
    public override string Description => "Hash any passwords that were stored in plain text";

    public override async Task UpAsync(DbContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        // This example shows using EF entities for complex logic
        // For bulk operations, raw SQL is much faster

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, PasswordHash FROM Users WHERE PasswordHash NOT LIKE 'AQ%'"; // Identity hashes start with AQ

        var usersToUpdate = new List<(string Id, string NewHash)>();

        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(0);
                var plainPassword = reader.GetString(1);

                // Hash the password using ASP.NET Identity's hasher
                // In real code, you'd inject IPasswordHasher<User>
                var hashedPassword = HashPassword(plainPassword);
                usersToUpdate.Add((id, hashedPassword));
            }
        }

        foreach (var (id, newHash) in usersToUpdate)
        {
            using var updateCommand = connection.CreateCommand();
            updateCommand.CommandText = "UPDATE Users SET PasswordHash = @hash WHERE Id = @id";

            var hashParam = updateCommand.CreateParameter();
            hashParam.ParameterName = "@hash";
            hashParam.Value = newHash;
            updateCommand.Parameters.Add(hashParam);

            var idParam = updateCommand.CreateParameter();
            idParam.ParameterName = "@id";
            idParam.Value = id;
            updateCommand.Parameters.Add(idParam);

            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string HashPassword(string password)
    {
        // Simplified - in reality use PasswordHasher<TUser>
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(password);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}
