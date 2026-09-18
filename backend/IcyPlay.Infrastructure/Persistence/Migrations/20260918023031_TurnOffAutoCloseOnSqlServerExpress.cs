using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Turns AUTO_CLOSE off, because SQL Server Express turns it on.
    ///
    /// AUTO_CLOSE shuts the database down when the last connection drops and
    /// recovers it on the next one, throwing away the buffer pool and the plan
    /// cache each time. Against a connection pool that reads as a query which
    /// is instant whenever you measure it and times out for the customer: the
    /// court listing was taking thirty seconds on a cold start and seven
    /// milliseconds once warm, on twenty-six rows.
    ///
    /// It belongs in a migration rather than in a runbook because Express sets
    /// it on **every** database it creates, whatever the model database says.
    /// That is every developer's copy and the integration test database on
    /// every run — a thing nobody would think to check, arriving switched on.
    /// </summary>
    public partial class TurnOffAutoCloseOnSqlServerExpress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Outside a transaction: ALTER DATABASE ... SET cannot run inside
            // one, and EF wraps a migration in a transaction by default.
            migrationBuilder.Sql(
                """
                DECLARE @statement nvarchar(max);

                -- Engine edition 5 is Azure SQL Database, which has no such
                -- setting; asking it would fail the deployment over a thing it
                -- has already done for us.
                IF SERVERPROPERTY('EngineEdition') <> 5
                   AND EXISTS (SELECT 1 FROM sys.databases
                               WHERE name = DB_NAME() AND is_auto_close_on = 1)
                BEGIN
                    -- The database name cannot be a parameter here, so it is
                    -- quoted into the statement. QUOTENAME is what makes that
                    -- safe against a name carrying a bracket.
                    SET @statement =
                        N'ALTER DATABASE ' + QUOTENAME(DB_NAME()) + N' SET AUTO_CLOSE OFF WITH NO_WAIT';

                    BEGIN TRY
                        EXEC sp_executesql @statement;
                    END TRY
                    BEGIN CATCH
                        -- Needs ALTER on the database, which a deployment
                        -- account is not always given. This is a performance
                        -- setting, not a schema change: say so and carry on
                        -- rather than stopping a release over it.
                        PRINT 'AUTO_CLOSE could not be turned off: ' + ERROR_MESSAGE();
                    END CATCH
                END
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. Down exists to undo a schema change so the
            // code above it can run again; putting AUTO_CLOSE back would only
            // reintroduce the stall, and nothing in the schema depends on it.
        }
    }
}
