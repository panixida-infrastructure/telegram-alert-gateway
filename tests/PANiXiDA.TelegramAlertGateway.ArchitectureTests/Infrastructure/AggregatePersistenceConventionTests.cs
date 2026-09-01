using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Infrastructure;

public sealed class AggregatePersistenceConventionTests
{
    private const string RepositorySuffix = "Repository";
    private const string WriteDbContextSuffix = "WriteDbContext";

    [Fact(DisplayName = "Aggregate roots should have registered repositories when declared")]
    public void AggregateRoots_Should_HaveRegisteredRepositories_When_Declared()
    {
        var aggregates = GetAggregatePersistence();
        var violations = aggregates
            .SelectMany(GetRepositoryViolations)
            .ToArray();

        Assert.NotEmpty(aggregates);
        Assert.True(
            violations.Length == 0,
            $"Aggregate repository registration violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Aggregate roots should have persistence configurations when declared")]
    public void AggregateRoots_Should_HavePersistenceConfigurations_When_Declared()
    {
        var aggregates = GetAggregatePersistence();
        var violations = aggregates
            .SelectMany(GetConfigurationViolations)
            .ToArray();

        Assert.NotEmpty(aggregates);
        Assert.True(
            violations.Length == 0,
            $"Aggregate persistence configuration violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static AggregatePersistence[] GetAggregatePersistence()
    {
        return
        [
            .. ArchitectureDefinition.Modules
                .SelectMany(module =>
                {
                    var infrastructureAssembly =
                        ArchitectureDefinition.ProductionAssemblies.Single(
                            assembly => string.Equals(
                                assembly.GetName().Name,
                                module.InfrastructureAssemblyName,
                                StringComparison.Ordinal));
                    var services = InfrastructureServiceCollectionFactory.Create(
                        infrastructureAssembly);
                    var infrastructureTypes =
                        infrastructureAssembly.GetTypes();
                    var writeDbContextTypes = infrastructureTypes
                        .Where(type =>
                            type is { IsClass: true, IsAbstract: false } &&
                            type.Name.EndsWith(
                                WriteDbContextSuffix,
                                StringComparison.Ordinal) &&
                            typeof(DbContext).IsAssignableFrom(type))
                        .ToArray();
                    var configuredEntityTypes =
                        GetConfiguredEntityTypes(
                            services,
                            writeDbContextTypes);

                    return InfrastructurePersistenceConvention
                        .GetAggregateRootTypes(module)
                        .Select(aggregateType =>
                        {
                            var featureName =
                                EnglishNamingConvention.Pluralize(
                                    aggregateType.Name);
                            var writeNamespace =
                                module.InfrastructureAssemblyName +
                                ".Persistence.Features." +
                                featureName +
                                ".Write";
                            var repositoryTypes = infrastructureTypes
                                .Where(type =>
                                    type is
                                    {
                                        IsClass: true,
                                        IsAbstract: false
                                    } &&
                                    string.Equals(
                                        type.Namespace,
                                        writeNamespace,
                                        StringComparison.Ordinal) &&
                                    type.Name.EndsWith(
                                        RepositorySuffix,
                                        StringComparison.Ordinal))
                                .ToArray();
                            var configurationTypes = infrastructureTypes
                                .Where(type =>
                                    type is
                                    {
                                        IsClass: true,
                                        IsAbstract: false
                                    } &&
                                    string.Equals(
                                        type.Namespace,
                                        writeNamespace,
                                        StringComparison.Ordinal) &&
                                    IsConfigurationFor(
                                        type,
                                        aggregateType))
                                .ToArray();

                            return new AggregatePersistence(
                                AggregateType: aggregateType,
                                RepositoryTypes: repositoryTypes,
                                RegisteredRepositoryTypes:
                                [
                                    .. repositoryTypes.Where(type =>
                                        IsRegistered(
                                            services,
                                            type))
                                ],
                                HasConfigurationDeclaration:
                                    configurationTypes.Length > 0 ||
                                    writeDbContextTypes.Any(type =>
                                        HasInlineConfiguration(
                                            type,
                                            aggregateType)),
                                ConfiguredEntityTypes:
                                    configuredEntityTypes);
                        });
                })
                .OrderBy(
                    aggregate => aggregate.AggregateType.FullName,
                    StringComparer.Ordinal)
        ];
    }

    private static IEnumerable<string> GetRepositoryViolations(
        AggregatePersistence aggregate)
    {
        if (aggregate.RepositoryTypes.Length != 1)
        {
            yield return
                $"{aggregate.AggregateType.FullName} must have exactly one " +
                $"repository in its Write feature root, found: " +
                $"{FormatTypes(aggregate.RepositoryTypes)}.";
            yield break;
        }

        if (aggregate.RegisteredRepositoryTypes.Length == 0)
        {
            yield return
                $"{aggregate.RepositoryTypes[0].FullName} must be registered " +
                $"in the module service collection.";
        }
    }

    private static IEnumerable<string> GetConfigurationViolations(
        AggregatePersistence aggregate)
    {
        if (!aggregate.HasConfigurationDeclaration)
        {
            yield return
                $"{aggregate.AggregateType.FullName} must have an EF Core " +
                $"entity configuration declaration.";
        }

        if (!IsConfigured(aggregate))
        {
            yield return
                $"{aggregate.AggregateType.FullName} must have its entity " +
                $"configuration registered in the module write DbContext.";
        }
    }

    private static bool IsRegistered(
        IServiceCollection serviceCollection,
        Type implementationType)
    {
        return serviceCollection.Any(descriptor =>
            descriptor.ImplementationType == implementationType ||
            descriptor.ServiceType == implementationType);
    }

    private static bool IsConfigurationFor(
        Type configurationType,
        Type aggregateType)
    {
        var configurationContract = InfrastructurePersistenceConvention
            .GetClosedGenericInterface(
                configurationType,
                typeof(IEntityTypeConfiguration<>));

        return configurationContract?.GetGenericArguments()[0] ==
               aggregateType;
    }

    private static bool HasInlineConfiguration(
        Type writeDbContextType,
        Type aggregateType)
    {
        var configuredTypeNames = InfrastructurePersistenceConvention
            .FindSourceFiles(writeDbContextType)
            .SelectMany(sourceFile => CSharpSyntaxTree
                .ParseText(File.ReadAllText(sourceFile))
                .GetRoot()
                .DescendantNodes()
                .OfType<GenericNameSyntax>())
            .Where(genericName =>
                string.Equals(
                    genericName.Identifier.ValueText,
                    "Entity",
                    StringComparison.Ordinal) &&
                genericName.TypeArgumentList.Arguments.Count == 1)
            .Select(genericName =>
                genericName.TypeArgumentList.Arguments[0].ToString())
            .Select(GetUnqualifiedTypeName)
            .ToArray();

        return configuredTypeNames.Contains(
                   aggregateType.Name,
                   StringComparer.Ordinal) ||
               configuredTypeNames.Contains(
                   "Application" + aggregateType.Name,
                   StringComparer.Ordinal);
    }

    private static string GetUnqualifiedTypeName(string typeName)
    {
        var separatorIndex = typeName.LastIndexOf('.');

        return separatorIndex < 0
            ? typeName
            : typeName[(separatorIndex + 1)..];
    }

    private static Type[] GetConfiguredEntityTypes(
        IServiceCollection serviceCollection,
        IReadOnlyCollection<Type> writeDbContextTypes)
    {
        using var serviceProvider =
            serviceCollection.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        return
        [
            .. writeDbContextTypes
                .Select(type =>
                    (DbContext)scope.ServiceProvider
                        .GetRequiredService(type))
                .SelectMany(dbContext => dbContext.Model.GetEntityTypes())
                .Select(entityType => entityType.ClrType)
                .Distinct()
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
        ];
    }

    private static bool IsConfigured(
        AggregatePersistence aggregate)
    {
        return aggregate.ConfiguredEntityTypes.Any(type =>
            string.Equals(
                type.Name,
                aggregate.AggregateType.Name,
                StringComparison.Ordinal) ||
            string.Equals(
                type.Name,
                "Application" + aggregate.AggregateType.Name,
                StringComparison.Ordinal));
    }

    private static string FormatTypes(
        Type[] types)
    {
        return types.Length == 0
            ? "<none>"
            : string.Join(
                ", ",
                types
                    .Select(type => type.FullName)
                    .Order(StringComparer.Ordinal));
    }

    private sealed record AggregatePersistence(
        Type AggregateType,
        Type[] RepositoryTypes,
        Type[] RegisteredRepositoryTypes,
        bool HasConfigurationDeclaration,
        Type[] ConfiguredEntityTypes);
}
