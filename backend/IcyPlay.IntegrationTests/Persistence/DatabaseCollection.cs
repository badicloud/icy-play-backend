namespace IcyPlay.IntegrationTests.Persistence;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DatabaseCollection : ICollectionFixture<SqlServerDatabaseFixture>
{
    public const string Name = "SQL Server database integration tests";
}
