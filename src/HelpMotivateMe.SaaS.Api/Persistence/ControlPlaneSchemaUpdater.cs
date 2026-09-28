using System.Data;
using Microsoft.EntityFrameworkCore;

namespace HelpMotivateMe.SaaS.Api.Persistence;

public static class ControlPlaneSchemaUpdater
{
    private static readonly IReadOnlyDictionary<string, string> Columns = new Dictionary<string, string>
    {
        ["OwnerEmail"] = "TEXT NOT NULL DEFAULT ''",
        ["AppName"] = "TEXT NOT NULL DEFAULT ''",
        ["Plan"] = "TEXT NOT NULL DEFAULT 'Free'",
        ["AiMode"] = "TEXT NOT NULL DEFAULT 'Disabled'",
        ["AiProvider"] = "TEXT NULL",
        ["AiModel"] = "TEXT NULL",
        ["HasAiApiKey"] = "INTEGER NOT NULL DEFAULT 0",
        ["LastLoginUtc"] = "TEXT NULL",
        ["TotalUptimeSeconds"] = "INTEGER NOT NULL DEFAULT 0"
    };

    public static async Task UpgradeAsync(ControlPlaneDbContext db, CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);

        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS "Backups" (
                        "Id" TEXT NOT NULL CONSTRAINT "PK_Backups" PRIMARY KEY,
                        "TenantId" TEXT NOT NULL,
                        "FileName" TEXT NOT NULL,
                        "SizeBytes" INTEGER NOT NULL,
                        "CreatedUtc" TEXT NOT NULL,
                        CONSTRAINT "FK_Backups_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE CASCADE
                    );
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    CREATE INDEX IF NOT EXISTS "IX_Backups_TenantId_CreatedUtc" ON "Backups" ("TenantId", "CreatedUtc");
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('Tenants')";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) existingColumns.Add(reader.GetString(1));
            }

            foreach (var (name, definition) in Columns)
            {
                if (existingColumns.Contains(name)) continue;

                await using var command = connection.CreateCommand();
                command.CommandText = $"ALTER TABLE Tenants ADD COLUMN {name} {definition}";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE Tenants SET AppName = Subdomain WHERE AppName = ''";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }
}