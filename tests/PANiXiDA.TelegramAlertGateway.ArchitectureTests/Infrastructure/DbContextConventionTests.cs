using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Infrastructure;

public sealed class DbContextConventionTests
{
    private const string ReadDbContextSuffix = "ReadDbContext";
    private const string WriteDbContextSuffix = "WriteDbContext";

    [Fact(DisplayName = "Read database contexts should match module names and reside in Persistence Core when declared")]
    public void ReadDatabaseContexts_Should_MatchModuleNamesAndResideInPersistenceCore_When_Declared()
    {
        var contexts = GetDatabaseContexts(typeof(ReadDbContext<>));
        var violations = contexts
            .SelectMany(context => GetContextViolations(
                context,
                ReadDbContextSuffix))
            .ToArray();

        Assert.NotEmpty(contexts);
        Assert.True(
            violations.Length == 0,
            $"Read database context convention violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Write database contexts should match module names and reside in Persistence Core when declared")]
    public void WriteDatabaseContexts_Should_MatchModuleNamesAndResideInPersistenceCore_When_Declared()
    {
        var contexts = GetDatabaseContexts(typeof(WriteDbContext<>));
        var violations = contexts
            .SelectMany(context => GetContextViolations(
                context,
                WriteDbContextSuffix))
            .ToArray();

        Assert.NotEmpty(contexts);
        Assert.True(
            violations.Length == 0,
            $"Write database context convention violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static Type[] GetDatabaseContexts(Type openGenericContextType)
    {
        return InfrastructurePersistenceConvention
            .GetConcreteInfrastructureTypes(type =>
                InfrastructurePersistenceConvention
                    .GetClosedGenericBaseType(
                        type,
                        openGenericContextType) is not null);
    }

    private static List<string> GetContextViolations(
        Type context,
        string expectedSuffix)
    {
        var module =
            InfrastructurePersistenceConvention.GetModule(context);
        var expectedName =
            InfrastructurePersistenceConvention.GetModuleShortName(module) +
            expectedSuffix;
        var violations = new List<string>();

        if (!string.Equals(
                context.Name,
                expectedName,
                StringComparison.Ordinal))
        {
            violations.Add(
                $"{context.FullName} must be named '{expectedName}'.");
        }

        violations.AddRange(
            InfrastructurePersistenceConvention.GetLocationViolations(
                context,
                "Persistence",
                "Core"));

        return violations;
    }
}
