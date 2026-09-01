using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Infrastructure;

public sealed class MigrationConventionTests
{
    [Fact(DisplayName = "Migrations and model snapshots should reside in Persistence Core Migrations when declared")]
    public void MigrationsAndModelSnapshots_Should_ResideInPersistenceCoreMigrations_When_Declared()
    {
        var migrationTypes = InfrastructurePersistenceConvention
            .GetConcreteInfrastructureTypes(type =>
                typeof(Migration).IsAssignableFrom(type) ||
                typeof(ModelSnapshot).IsAssignableFrom(type));
        var violations = migrationTypes
            .SelectMany(type =>
                InfrastructurePersistenceConvention.GetLocationViolations(
                    type,
                    "Persistence",
                    "Core",
                    "Migrations"))
            .ToArray();

        Assert.NotEmpty(migrationTypes);
        Assert.True(
            violations.Length == 0,
            $"Migration location violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }
}
