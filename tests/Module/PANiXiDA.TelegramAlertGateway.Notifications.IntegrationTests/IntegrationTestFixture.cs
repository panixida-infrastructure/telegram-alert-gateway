using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.DependencyInjection;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core;
using PANiXiDA.TelegramAlertGateway.Testing.Databases;

namespace PANiXiDA.TelegramAlertGateway.Notifications.IntegrationTests;

public sealed class IntegrationTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlTestDatabase _database = new();

    private ServiceProvider _serviceProvider = null!;

    private static string PostgreSqlConnectionStringConfigurationKey =>
        PostgreSqlTestDatabase.PostgreSqlConnectionStringEnvironmentVariable.Replace(
            "__",
            ConfigurationPath.KeyDelimiter,
            StringComparison.Ordinal);

    public string ConnectionString => _database.PostgreSqlConnectionString;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PostgreSqlConnectionStringConfigurationKey] = ConnectionString,
                ["AlertRouting:DefaultTopic"] = "unclassified",
                ["AlertRouting:Rules:0:Topic"] = "tactical-heroes",
                ["AlertRouting:Rules:0:Matches:0"] = "tactical-heroes",
                ["AlertRouting:Rules:1:Topic"] = "dotnet-template",
                ["AlertRouting:Rules:1:Matches:0"] = "dotnet-template",
                ["AlertRouting:Rules:2:Topic"] = "observability",
                ["AlertRouting:Rules:2:Matches:0"] = "grafana",
                ["AlertRouting:Rules:2:Matches:1"] = "telegram-alert-gateway",
                ["AlertRouting:Rules:3:Topic"] = "core-platform",
                ["AlertRouting:Rules:3:Matches:0"] = "core-platform",
                ["AlertRouting:Rules:3:Matches:1"] = "csi-driver-timeweb-cloud",
                ["AlertRouting:Rules:4:Topic"] = "tests",
                ["AlertRouting:Rules:4:Matches:0"] = "telegram-alert-gateway-smoke",
                ["VictoriaLogs:Endpoint"] = "http://victorialogs.test",
                ["VictoriaLogs:GrafanaLogsUrl"] = "https://grafana.panixida.ru",
                ["NotificationRetention:BatchSize"] = "2"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        _serviceProvider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true
            });

        await using var scope = CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsWriteDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public AsyncServiceScope CreateScope()
    {
        return _serviceProvider.CreateAsyncScope();
    }

    public Task ResetDatabaseAsync(CancellationToken cancellationToken)
    {
        return _database.ResetPostgreSqlDatabaseAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
        }

        await _database.DisposeAsync();
    }
}
