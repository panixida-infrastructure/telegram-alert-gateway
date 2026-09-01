using System.Reflection;

using PANiXiDA.Core.Application.Querying;
using PANiXiDA.Core.Application.Querying.Cursor;
using PANiXiDA.Core.Application.Querying.Pagination;
using PANiXiDA.Core.ResultPattern;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Definitions;

internal static class ReadSideConvention
{
    private const string ApplicationAssemblySuffix = ".Application";
    private const string CoreApplicationAssemblyName =
        "PANiXiDA.Core.Application";
    private const string CoreApplicationQueryingNamespace =
        "PANiXiDA.Core.Application.Querying";
    private const string CoreDomainAssemblyName = "PANiXiDA.Core.Domain";
    private const string DomainAssemblySuffix = ".Domain";

    internal static bool IsPrimitiveValue(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        return underlyingType.IsPrimitive ||
               underlyingType.IsEnum ||
               underlyingType == typeof(string) ||
               underlyingType == typeof(decimal) ||
               underlyingType == typeof(Guid) ||
               underlyingType == typeof(DateTime) ||
               underlyingType == typeof(DateTimeOffset) ||
               underlyingType == typeof(DateOnly) ||
               underlyingType == typeof(TimeOnly) ||
               underlyingType == typeof(TimeSpan);
    }

    internal static bool IsAllowedReadInput(Type type)
    {
        return IsAllowedReadInput(type, new HashSet<Type>());
    }

    internal static bool IsReadModelResult(Type type)
    {
        var readModelTypes = new List<Type>();

        return TryCollectReadModelTypes(
                   type,
                   readModelTypes,
                   new HashSet<Type>()) &&
               readModelTypes.Count > 0 &&
               readModelTypes.All(type =>
                   typeof(IReadModel).IsAssignableFrom(type));
    }

    private static bool IsAllowedReadInput(
        Type type,
        ISet<Type> visitedTypes)
    {
        if (!visitedTypes.Add(type))
        {
            return true;
        }

        if (IsDomainType(type) ||
            typeof(IReadModel).IsAssignableFrom(type))
        {
            return false;
        }

        if (IsPrimitiveValue(type) ||
            type == typeof(CancellationToken))
        {
            return true;
        }

        var nullableType = Nullable.GetUnderlyingType(type);

        if (nullableType is not null)
        {
            return IsAllowedReadInput(nullableType, visitedTypes);
        }

        if (type.HasElementType)
        {
            return IsAllowedReadInput(
                type.GetElementType()
                    ?? throw new InvalidOperationException(
                        $"Could not determine element type for '{type}'."),
                visitedTypes);
        }

        var collectionElementType = GetCollectionElementType(type);

        if (collectionElementType is not null)
        {
            return IsAllowedReadInput(
                collectionElementType,
                visitedTypes);
        }

        if (!IsApplicationParameterModel(type))
        {
            return false;
        }

        var stateTypes = type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Select(property => property.PropertyType)
            .Concat(type
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Select(field => field.FieldType))
            .Distinct()
            .ToArray();

        return stateTypes.Length == 0
            ? IsCoreApplicationQueryingType(type)
            : stateTypes.All(stateType =>
                IsAllowedReadInput(stateType, visitedTypes));
    }

    private static bool TryCollectReadModelTypes(
        Type type,
        ICollection<Type> readModelTypes,
        ISet<Type> visitedTypes)
    {
        if (!visitedTypes.Add(type))
        {
            return true;
        }

        if (typeof(IReadModel).IsAssignableFrom(type))
        {
            readModelTypes.Add(type);

            return true;
        }

        if (type.HasElementType)
        {
            return TryCollectReadModelTypes(
                type.GetElementType()
                    ?? throw new InvalidOperationException(
                        $"Could not determine element type for '{type}'."),
                readModelTypes,
                visitedTypes);
        }

        var collectionElementType = GetCollectionElementType(type);

        if (collectionElementType is not null)
        {
            return TryCollectReadModelTypes(
                collectionElementType,
                readModelTypes,
                visitedTypes);
        }

        if (!type.IsGenericType ||
            !IsSupportedResultWrapper(type.GetGenericTypeDefinition()))
        {
            return false;
        }

        var genericArguments = type.GetGenericArguments();

        return genericArguments.Length == 1 &&
               TryCollectReadModelTypes(
                   genericArguments[0],
                   readModelTypes,
                   visitedTypes);
    }

    private static bool IsDomainType(Type type)
    {
        var assemblyName = type.Assembly.GetName().Name;

        return string.Equals(
                   assemblyName,
                   CoreDomainAssemblyName,
                   StringComparison.Ordinal) ||
               assemblyName?.EndsWith(
                   DomainAssemblySuffix,
                   StringComparison.Ordinal) == true;
    }

    private static bool IsApplicationParameterModel(Type type)
    {
        var assemblyName = type.Assembly.GetName().Name;

        return assemblyName?.EndsWith(
                   ApplicationAssemblySuffix,
                   StringComparison.Ordinal) == true ||
               IsCoreApplicationQueryingType(type);
    }

    private static bool IsCoreApplicationQueryingType(Type type)
    {
        return string.Equals(
                   type.Assembly.GetName().Name,
                   CoreApplicationAssemblyName,
                   StringComparison.Ordinal) &&
               (string.Equals(
                    type.Namespace,
                    CoreApplicationQueryingNamespace,
                    StringComparison.Ordinal) ||
                type.Namespace?.StartsWith(
                    CoreApplicationQueryingNamespace + ".",
                    StringComparison.Ordinal) == true);
    }

    private static Type? GetCollectionElementType(Type type)
    {
        return type
            .GetInterfaces()
            .Append(type)
            .Where(candidate => candidate.IsGenericType)
            .FirstOrDefault(candidate =>
                candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }

    private static bool IsSupportedResultWrapper(
        Type genericTypeDefinition)
    {
        return genericTypeDefinition == typeof(Task<>) ||
               genericTypeDefinition == typeof(ValueTask<>) ||
               genericTypeDefinition == typeof(Result<>) ||
               genericTypeDefinition == typeof(PaginationResult<>) ||
               genericTypeDefinition == typeof(CursorPaginationResult<>);
    }
}
