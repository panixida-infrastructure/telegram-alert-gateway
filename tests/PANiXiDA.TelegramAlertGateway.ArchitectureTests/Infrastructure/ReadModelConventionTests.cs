using System.Reflection;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Infrastructure;

public sealed class ReadModelConventionTests
{
    private const string ReadDbModelSuffix = "ReadDbModel";
    private const string ReadModelMapperSuffix = "ReadModelMapper";

    [Fact(DisplayName = "Read model mappers should end with ReadModelMapper when declared")]
    public void ReadModelMappers_Should_EndWithReadModelMapper_When_Declared()
    {
        var mappers = GetReadModelMappers();
        var violations = mappers
            .Where(type => !type.Name.EndsWith(
                ReadModelMapperSuffix,
                StringComparison.Ordinal))
            .Select(type =>
                $"{type.FullName} must end with " +
                $"'{ReadModelMapperSuffix}'.")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Read model mapper naming violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Read model mappers should reside in aggregate Read Mappers directories when declared")]
    public void ReadModelMappers_Should_ResideInAggregateReadMappersDirectories_When_Declared()
    {
        var mappers = GetReadModelMappers();
        var violations = mappers
            .SelectMany(GetMapperLocationViolations)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Read model mapper location violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Read database models should end with ReadDbModel when declared")]
    public void ReadDatabaseModels_Should_EndWithReadDbModel_When_Declared()
    {
        var readDbModels = GetReadDatabaseModels();
        var violations = readDbModels
            .Where(type => !type.Name.EndsWith(
                ReadDbModelSuffix,
                StringComparison.Ordinal))
            .Select(type =>
                $"{type.FullName} must end with '{ReadDbModelSuffix}'.")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Read database model naming violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Read database models should reside in aggregate Read DbModels directories when declared")]
    public void ReadDatabaseModels_Should_ResideInAggregateReadDbModelsDirectories_When_Declared()
    {
        var readDbModels = GetReadDatabaseModels();
        var violations = readDbModels
            .SelectMany(type =>
                InfrastructurePersistenceConvention
                    .GetAggregateFeatureLocationViolations(
                        type,
                        "Read",
                        "DbModels"))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Read database model location violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Read database model aggregate foreign keys should have bidirectional navigations when declared")]
    public void ReadDatabaseModelAggregateForeignKeys_Should_HaveBidirectionalNavigations_When_Declared()
    {
        var readDbModels = GetReadDatabaseModels();
        var violations = readDbModels
            .SelectMany(readDbModel =>
                GetAggregateNavigationViolations(
                    readDbModel,
                    readDbModels))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Read database model navigation violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static Type[] GetReadModelMappers()
    {
        return InfrastructurePersistenceConvention
            .GetConcreteInfrastructureTypes(type =>
                InfrastructurePersistenceConvention
                    .GetClosedGenericInterface(
                        type,
                        typeof(IReadModelMapper<,,>)) is not null);
    }

    private static Type[] GetReadDatabaseModels()
    {
        return InfrastructurePersistenceConvention
            .GetConcreteInfrastructureTypes(type =>
                InfrastructurePersistenceConvention
                    .GetClosedGenericBaseType(
                        type,
                        typeof(ReadDbModel<>)) is not null ||
                InfrastructurePersistenceConvention
                    .GetClosedGenericBaseType(
                        type,
                        typeof(AuditableReadDbModel<>)) is not null);
    }

    private static IEnumerable<string> GetMapperLocationViolations(
        Type mapper)
    {
        var mapperContract = InfrastructurePersistenceConvention
            .GetClosedGenericInterface(
                mapper,
                typeof(IReadModelMapper<,,>))
            ?? throw new InvalidOperationException(
                $"Could not determine read model mapper contract for " +
                $"'{mapper.FullName}'.");
        var readDbModelType = mapperContract.GetGenericArguments()[1];
        var aggregateName = readDbModelType.Name.EndsWith(
            ReadDbModelSuffix,
            StringComparison.Ordinal)
            ? readDbModelType.Name[..^ReadDbModelSuffix.Length]
            : readDbModelType.Name;
        var module =
            InfrastructurePersistenceConvention.GetModule(mapper);
        var aggregateType = InfrastructurePersistenceConvention
            .GetAggregateRootTypes(module)
            .SingleOrDefault(type => string.Equals(
                type.Name,
                aggregateName,
                StringComparison.Ordinal));

        if (aggregateType is null)
        {
            return
            [
                $"{mapper.FullName} maps '{readDbModelType.FullName}', which " +
                $"must correspond to an aggregate root in module " +
                $"'{module.Name}'."
            ];
        }

        var featureName =
            EnglishNamingConvention.Pluralize(aggregateType.Name);

        return InfrastructurePersistenceConvention.GetLocationViolations(
            mapper,
            "Persistence",
            "Features",
            featureName,
            "Read",
            "Mappers");
    }

    private static IEnumerable<string> GetAggregateNavigationViolations(
        Type dependentReadDbModel,
        IReadOnlyCollection<Type> readDbModels)
    {
        var module =
            InfrastructurePersistenceConvention.GetModule(dependentReadDbModel);
        var aggregateNames = InfrastructurePersistenceConvention
            .GetAggregateRootTypes(module)
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var foreignKey in dependentReadDbModel
                     .GetProperties()
                     .Where(property =>
                         property.Name.Length > "Id".Length &&
                         property.Name.EndsWith("Id", StringComparison.Ordinal)))
        {
            var aggregateName = foreignKey.Name[..^"Id".Length];

            if (!aggregateNames.Contains(aggregateName))
            {
                continue;
            }

            var principalReadDbModel = readDbModels.SingleOrDefault(type =>
                type.Assembly == dependentReadDbModel.Assembly &&
                string.Equals(
                    type.Name,
                    aggregateName + ReadDbModelSuffix,
                    StringComparison.Ordinal));

            if (principalReadDbModel is null)
            {
                yield return
                    $"{dependentReadDbModel.FullName}.{foreignKey.Name} " +
                    $"references aggregate '{aggregateName}', but its " +
                    $"{ReadDbModelSuffix} is not declared in the module.";
                continue;
            }

            var referenceNavigation = dependentReadDbModel
                .GetProperties()
                .SingleOrDefault(property =>
                    property.PropertyType == principalReadDbModel);

            if (referenceNavigation is null)
            {
                yield return
                    $"{dependentReadDbModel.FullName}.{foreignKey.Name} " +
                    $"must have a reference navigation to " +
                    $"{principalReadDbModel.FullName}.";
            }
            else if (new NullabilityInfoContext()
                         .Create(referenceNavigation)
                         .ReadState != NullabilityState.Nullable)
            {
                yield return
                    $"{dependentReadDbModel.FullName}." +
                    $"{referenceNavigation.Name} reference navigation " +
                    $"must be nullable.";
            }

            if (!principalReadDbModel
                    .GetProperties()
                    .Any(property => IsCollectionOf(
                        property.PropertyType,
                        dependentReadDbModel)))
            {
                yield return
                    $"{principalReadDbModel.FullName} must have a collection " +
                    $"navigation to {dependentReadDbModel.FullName} for " +
                    $"{dependentReadDbModel.FullName}.{foreignKey.Name}.";
            }
        }
    }

    private static bool IsCollectionOf(
        Type propertyType,
        Type elementType)
    {
        return propertyType
            .GetInterfaces()
            .Append(propertyType)
            .Any(type =>
                type.IsGenericType &&
                type.GetGenericTypeDefinition() == typeof(ICollection<>) &&
                type.GenericTypeArguments[0] == elementType);
    }
}
