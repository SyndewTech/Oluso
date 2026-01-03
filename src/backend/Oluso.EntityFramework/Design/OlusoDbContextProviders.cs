using Microsoft.EntityFrameworkCore;

namespace Oluso.EntityFramework.Design;

/// <summary>
/// SQLite-specific DbContext for migrations and runtime.
/// Register this context when using SQLite to ensure migrations are found.
/// </summary>
public class OlusoDbContextSqlite : OlusoDbContext
{
    /// <summary>
    /// Design-time constructor for EF Core tooling.
    /// </summary>
    public OlusoDbContextSqlite() : base(CreateDesignTimeOptions()) { }

    /// <summary>
    /// Runtime constructor for DI.
    /// </summary>
    public OlusoDbContextSqlite(DbContextOptions<OlusoDbContextSqlite> options)
        : base(options) { }

    private static DbContextOptions<OlusoDbContext> CreateDesignTimeOptions()
    {
        var builder = new DbContextOptionsBuilder<OlusoDbContext>();
        builder.UseSqlite("Data Source=:memory:");
        return builder.Options;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite("Data Source=Oluso.db");
        }
    }
}

/// <summary>
/// SQL Server-specific DbContext for migrations and runtime.
/// Register this context when using SQL Server to ensure migrations are found.
/// </summary>
public class OlusoDbContextSqlServer : OlusoDbContext
{
    /// <summary>
    /// Design-time constructor for EF Core tooling.
    /// </summary>
    public OlusoDbContextSqlServer() : base(CreateDesignTimeOptions()) { }

    /// <summary>
    /// Runtime constructor for DI.
    /// </summary>
    public OlusoDbContextSqlServer(DbContextOptions<OlusoDbContextSqlServer> options)
        : base(options) { }

    private static DbContextOptions<OlusoDbContext> CreateDesignTimeOptions()
    {
        var builder = new DbContextOptionsBuilder<OlusoDbContext>();
        builder.UseSqlServer("Server=.;Database=Oluso;Trusted_Connection=True;TrustServerCertificate=True");
        return builder.Options;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlServer("Server=.;Database=Oluso;Trusted_Connection=True;TrustServerCertificate=True");
        }
    }
}

/// <summary>
/// PostgreSQL-specific DbContext for migrations and runtime.
/// Register this context when using PostgreSQL to ensure migrations are found.
/// </summary>
public class OlusoDbContextPostgres : OlusoDbContext
{
    /// <summary>
    /// Design-time constructor for EF Core tooling.
    /// </summary>
    public OlusoDbContextPostgres() : base(CreateDesignTimeOptions()) { }

    /// <summary>
    /// Runtime constructor for DI.
    /// </summary>
    public OlusoDbContextPostgres(DbContextOptions<OlusoDbContextPostgres> options)
        : base(options) { }

    private static DbContextOptions<OlusoDbContext> CreateDesignTimeOptions()
    {
        var builder = new DbContextOptionsBuilder<OlusoDbContext>();
        builder.UseNpgsql("Host=localhost;Database=Oluso;Username=postgres;Password=postgres");
        return builder.Options;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql("Host=localhost;Database=Oluso;Username=postgres;Password=postgres");
        }
    }
}
