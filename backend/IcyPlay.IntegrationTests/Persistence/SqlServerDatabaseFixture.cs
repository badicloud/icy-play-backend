using IcyPlay.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.IntegrationTests.Persistence;

public sealed class SqlServerDatabaseFixture : IAsyncLifetime
{
    private const string TestDatabaseName = "IcyPlayIntegrationTests";

    private readonly DbContextOptions<AppDbContext> options;

    public SqlServerDatabaseFixture()
    {
        var connectionString = Environment.GetEnvironmentVariable("ICYPLAY_TEST_DB_CONNECTION") ??
            $"Server=localhost\\SQLEXPRESS;Database={TestDatabaseName};Trusted_Connection=True;TrustServerCertificate=True;";
        var connectionStringBuilder = new SqlConnectionStringBuilder(connectionString);

        if (!string.Equals(
                connectionStringBuilder.InitialCatalog,
                TestDatabaseName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The integration test connection must target the '{TestDatabaseName}' database.");
        }

        options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionStringBuilder.ConnectionString)
            .Options;
    }

    public AppDbContext CreateContext() => new(options);

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}
