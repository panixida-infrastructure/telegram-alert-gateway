using Npgsql;

using Respawn;
using Respawn.Graph;

using Testcontainers.PostgreSql;

namespace PANiXiDA.TelegramAlertGateway.Testing.Databases;

public sealed class PostgreSqlTestDatabase : IAsyncDisposable
{
    public const string PostgreSqlConnectionStringEnvironmentVariable =
        "ConnectionStrings__PostgreSqlConnectionString";

    private PostgreSqlContainer? _postgreSqlContainer;
    private string? _postgreSqlConnectionString;

    public string PostgreSqlConnectionString =>
        _postgreSqlConnectionString ??
        throw new InvalidOperationException("PostgreSQL test database is not initialized.");

    public async ValueTask InitializeAsync(
        CancellationToken cancellationToken)
    {
        var configuredConnectionString = Environment.GetEnvironmentVariable(
            PostgreSqlConnectionStringEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            _postgreSqlConnectionString = configuredConnectionString;

            return;
        }

        _postgreSqlContainer = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("organization_product_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _postgreSqlContainer.StartAsync(cancellationToken);
        _postgreSqlConnectionString = _postgreSqlContainer.GetConnectionString();
    }

    public async Task ResetPostgreSqlDatabaseAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlConnectionString);
        await connection.OpenAsync(cancellationToken);

        var respawner = await Respawner.CreateAsync(
            connection,
            new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                TablesToIgnore =
                [
                    new Table("__EFMigrationsHistory")
                ]
            });

        await respawner.ResetAsync(connection);
    }

    public async ValueTask DisposeAsync()
    {
        if (_postgreSqlContainer is not null)
        {
            await _postgreSqlContainer.DisposeAsync();
        }
    }
}
