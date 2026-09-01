using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Domain.Abstractions;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Infrastructure;

public sealed class RepositoryImplementationConventionTests
{
    private const string ReadDbModelSuffix = "ReadDbModel";
    private const string ReadRepositorySuffix = "ReadRepository";
    private const string RepositorySuffix = "Repository";

    [Fact(DisplayName = "Repository interfaces should have exactly one implementation when declared")]
    public void RepositoryInterfaces_Should_HaveExactlyOneImplementation_When_Declared()
    {
        var repositoryContracts = GetRepositoryContracts();
        var violations = repositoryContracts
            .Where(contract => contract.Implementations.Length != 1)
            .Select(contract =>
                $"{contract.Interface.FullName} must have exactly one " +
                $"concrete Infrastructure implementation, found: " +
                $"{FormatTypes(contract.Implementations)}.")
            .ToArray();

        Assert.NotEmpty(repositoryContracts);
        Assert.True(
            violations.Length == 0,
            $"Repository implementation count violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Repository implementations should use plural aggregate names when declared")]
    public void RepositoryImplementations_Should_UsePluralAggregateNames_When_Declared()
    {
        var repositories = GetWriteRepositoryImplementations();
        var violations = repositories
            .SelectMany(GetNamingViolations)
            .ToArray();

        Assert.NotEmpty(repositories);
        Assert.True(
            violations.Length == 0,
            $"Repository implementation naming violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Read repository implementations should use plural aggregate names when declared")]
    public void ReadRepositoryImplementations_Should_UsePluralAggregateNames_When_Declared()
    {
        var repositories = GetReadRepositoryImplementations();
        var violations = repositories
            .SelectMany(GetNamingViolations)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Read repository implementation naming violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Repository implementations should reside in Write roots when declared")]
    public void RepositoryImplementations_Should_ResideInWriteRoots_When_Declared()
    {
        var repositories = GetWriteRepositoryImplementations();
        var violations = repositories
            .SelectMany(repository => GetLocationViolations(
                repository,
                "Write"))
            .ToArray();

        Assert.NotEmpty(repositories);
        Assert.True(
            violations.Length == 0,
            $"Repository implementation location violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Read repository implementations should reside in Read roots when declared")]
    public void ReadRepositoryImplementations_Should_ResideInReadRoots_When_Declared()
    {
        var repositories = GetReadRepositoryImplementations();
        var violations = repositories
            .SelectMany(repository => GetLocationViolations(
                repository,
                "Read"))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Read repository implementation location violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static RepositoryAdapter[] GetWriteRepositoryImplementations()
    {
        return
        [
            .. InfrastructurePersistenceConvention
                .GetConcreteInfrastructureTypes(type =>
                    InfrastructurePersistenceConvention
                        .GetClosedGenericInterface(
                            type,
                            typeof(IRepository<,>)) is not null)
                .Select(type =>
                {
                    var repositoryContract =
                        InfrastructurePersistenceConvention
                            .GetClosedGenericInterface(
                                type,
                                typeof(IRepository<,>))
                        ?? throw new InvalidOperationException(
                            $"Could not determine repository contract for " +
                            $"'{type.FullName}'.");
                    var aggregateType =
                        repositoryContract.GetGenericArguments()[1];
                    var featureName =
                        EnglishNamingConvention.Pluralize(
                            aggregateType.Name);

                    return new RepositoryAdapter(
                        Type: type,
                        FeatureName: featureName,
                        ExpectedName: featureName + RepositorySuffix);
                })
        ];
    }

    private static RepositoryAdapter[] GetReadRepositoryImplementations()
    {
        return
        [
            .. InfrastructurePersistenceConvention
                .GetConcreteInfrastructureTypes(type =>
                    InfrastructurePersistenceConvention
                        .GetClosedGenericInterface(
                            type,
                            typeof(IReadRepository<>)) is not null)
                .Select(type =>
                {
                    var featureName = GetReadRepositoryFeatureName(type);

                    return new RepositoryAdapter(
                        Type: type,
                        FeatureName: featureName,
                        ExpectedName: featureName is null
                            ? null
                            : featureName + ReadRepositorySuffix);
                })
        ];
    }

    private static string? GetReadRepositoryFeatureName(
        Type implementation)
    {
        var module =
            InfrastructurePersistenceConvention.GetModule(implementation);
        var aggregateTypes = InfrastructurePersistenceConvention
            .GetAggregateRootTypes(module);
        var efReadRepository = InfrastructurePersistenceConvention
            .GetClosedGenericBaseType(
                implementation,
                typeof(EfReadRepository<,,>));

        if (efReadRepository is not null)
        {
            var readDbModelType =
                efReadRepository.GetGenericArguments()[2];
            var aggregateName = readDbModelType.Name.EndsWith(
                ReadDbModelSuffix,
                StringComparison.Ordinal)
                ? readDbModelType.Name[..^ReadDbModelSuffix.Length]
                : readDbModelType.Name;
            var aggregateType = aggregateTypes.SingleOrDefault(type =>
                string.Equals(
                    type.Name,
                    aggregateName,
                    StringComparison.Ordinal));

            if (aggregateType is not null)
            {
                return EnglishNamingConvention.Pluralize(
                    aggregateType.Name);
            }
        }

        return aggregateTypes
            .Select(type => EnglishNamingConvention.Pluralize(type.Name))
            .SingleOrDefault(featureName =>
                implementation.GetInterfaces().Any(interfaceType =>
                    string.Equals(
                        interfaceType.Name,
                        "I" + featureName + ReadRepositorySuffix,
                        StringComparison.Ordinal)));
    }

    private static RepositoryContract[] GetRepositoryContracts()
    {
        return
        [
            .. ArchitectureDefinition.Modules
                .SelectMany(module =>
                {
                    var domainAssembly =
                        ArchitectureDefinition.ProductionAssemblies.Single(
                            assembly => string.Equals(
                                assembly.GetName().Name,
                                module.DomainAssemblyName,
                                StringComparison.Ordinal));
                    var applicationAssembly =
                        ArchitectureDefinition.ProductionAssemblies.Single(
                            assembly => string.Equals(
                                assembly.GetName().Name,
                                module.ApplicationAssemblyName,
                                StringComparison.Ordinal));
                    var infrastructureAssembly =
                        ArchitectureDefinition.ProductionAssemblies.Single(
                            assembly => string.Equals(
                                assembly.GetName().Name,
                                module.InfrastructureAssemblyName,
                                StringComparison.Ordinal));
                    var implementationTypes = infrastructureAssembly
                        .GetTypes()
                        .Where(type =>
                            type is
                            {
                                IsClass: true,
                                IsAbstract: false
                            })
                        .ToArray();
                    var repositoryInterfaces = domainAssembly
                        .GetTypes()
                        .Where(type =>
                            type.IsInterface &&
                            InfrastructurePersistenceConvention
                                .GetClosedGenericInterface(
                                    type,
                                    typeof(IRepository<,>)) is not null)
                        .Concat(applicationAssembly
                            .GetTypes()
                            .Where(type =>
                                type.IsInterface &&
                                InfrastructurePersistenceConvention
                                    .GetClosedGenericInterface(
                                        type,
                                        typeof(IReadRepository<>)) is not null));

                    return repositoryInterfaces.Select(
                        repositoryInterface => new RepositoryContract(
                            Interface: repositoryInterface,
                            Implementations:
                            [
                                .. implementationTypes.Where(
                                    repositoryInterface.IsAssignableFrom)
                            ]));
                })
                .OrderBy(
                    contract => contract.Interface.FullName,
                    StringComparer.Ordinal)
        ];
    }

    private static IEnumerable<string> GetNamingViolations(
        RepositoryAdapter repository)
    {
        if (repository.ExpectedName is null)
        {
            return
            [
                $"{repository.Type.FullName} must correspond to an aggregate " +
                $"root in its module."
            ];
        }

        return string.Equals(
            repository.Type.Name,
            repository.ExpectedName,
            StringComparison.Ordinal)
            ? []
            :
            [
                $"{repository.Type.FullName} must be named " +
                $"'{repository.ExpectedName}'."
            ];
    }

    private static List<string> GetLocationViolations(
        RepositoryAdapter repository,
        string accessMode)
    {
        if (repository.FeatureName is null)
        {
            return
            [
                $"{repository.Type.FullName} must correspond to an aggregate " +
                $"root before its location can be validated."
            ];
        }

        return
        [
            .. InfrastructurePersistenceConvention.GetLocationViolations(
                repository.Type,
                "Persistence",
                "Features",
                repository.FeatureName,
                accessMode)
        ];
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

    private sealed record RepositoryAdapter(
        Type Type,
        string? FeatureName,
        string? ExpectedName);

    private sealed record RepositoryContract(
        Type Interface,
        Type[] Implementations);
}
