using System.Collections;
using System.Reflection;

using PANiXiDA.Core.Domain;
using PANiXiDA.Core.Domain.AggregateRoots;
using PANiXiDA.Core.Domain.Entities;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Domain;

public sealed class DomainEncapsulationConventionTests
{
    private const string DomainAssemblySuffix = ".Domain";

    [Fact(DisplayName = "Aggregate roots and entities should not declare public setters when domain state is declared")]
    public void AggregateRootsAndEntities_Should_NotDeclarePublicSetters_When_DomainStateIsDeclared()
    {
        var domainEntities = GetDomainEntities();
        var violations = domainEntities
            .SelectMany(type => type
                .GetProperties(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly)
                .Where(property =>
                    property.GetSetMethod(nonPublic: true) is
                    {
                        IsPublic: true
                    })
                .Select(property =>
                    $"{type.FullName}.{property.Name} must not have a " +
                    $"public setter."))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(domainEntities);
        Assert.True(
            violations.Length == 0,
            $"Public domain state setters:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Aggregate roots and entities should not expose mutable collections when public state is declared")]
    public void AggregateRootsAndEntities_Should_NotExposeMutableCollections_When_PublicStateIsDeclared()
    {
        var domainEntities = GetDomainEntities();
        var violations = domainEntities
            .SelectMany(GetPublicStateMembers)
            .Where(member =>
                ContainsMutableCollection(member.MemberType))
            .Select(member =>
                $"{member.DeclaringType.FullName}.{member.Name} exposes " +
                $"mutable collection type '{member.MemberType}'.")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(domainEntities);
        Assert.True(
            violations.Length == 0,
            $"Mutable collections exposed by domain types:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Aggregate roots should not contain other aggregate roots when state is declared")]
    public void AggregateRoots_Should_NotContainOtherAggregateRoots_When_StateIsDeclared()
    {
        var aggregateRoots = GetDomainEntities()
            .Where(type =>
                typeof(IAggregateRoot).IsAssignableFrom(type))
            .ToArray();
        var violations = aggregateRoots
            .SelectMany(type => type
                .GetFields(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly)
                .Where(field => GetContainedTypes(field.FieldType)
                    .Any(containedType =>
                        typeof(IAggregateRoot).IsAssignableFrom(
                            containedType)))
                .Select(field =>
                    $"{type.FullName}.{field.Name} must not contain " +
                    $"aggregate root type '{field.FieldType}'."))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(aggregateRoots);
        Assert.True(
            violations.Length == 0,
            $"Nested aggregate roots:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Domain objects should declare only private constructors when created through factories")]
    public void DomainObjects_Should_DeclareOnlyPrivateConstructors_When_CreatedThroughFactories()
    {
        var domainObjects = GetDomainTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false } &&
                (typeof(IEntity).IsAssignableFrom(type) ||
                 typeof(ValueObject).IsAssignableFrom(type)))
            .ToArray();
        var violations = domainObjects
            .SelectMany(type => type
                .GetConstructors(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly)
                .Where(constructor => !constructor.IsPrivate)
                .Select(constructor =>
                    $"{type.FullName} declares non-private constructor " +
                    $"'{constructor}' and must expose creation through a " +
                    $"factory method instead."))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(domainObjects);
        Assert.True(
            violations.Length == 0,
            $"Non-private Domain object constructors:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static Type[] GetDomainEntities()
    {
        return
        [
            .. GetDomainTypes()
                .Where(type =>
                    type is { IsClass: true, IsAbstract: false } &&
                    typeof(IEntity).IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
        ];
    }

    private static IEnumerable<Type> GetDomainTypes()
    {
        return ArchitectureDefinition.ProductionAssemblies
            .Where(assembly => assembly.GetName().Name?.EndsWith(
                DomainAssemblySuffix,
                StringComparison.Ordinal) == true)
            .SelectMany(assembly => assembly.GetTypes());
    }

    private static IEnumerable<PublicStateMember> GetPublicStateMembers(
        Type domainType)
    {
        var properties = domainType
            .GetProperties(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.DeclaredOnly)
            .Where(property =>
                property.GetMethod is { IsPublic: true })
            .Select(property => new PublicStateMember(
                domainType,
                property.Name,
                property.PropertyType));
        var fields = domainType
            .GetFields(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.DeclaredOnly)
            .Select(field => new PublicStateMember(
                domainType,
                field.Name,
                field.FieldType));
        var methods = domainType
            .GetMethods(
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.DeclaredOnly)
            .Where(method =>
                !method.IsSpecialName &&
                method.ReturnType != typeof(void))
            .Select(method => new PublicStateMember(
                domainType,
                method.Name + "()",
                method.ReturnType));

        return properties
            .Concat(fields)
            .Concat(methods);
    }

    private static bool ContainsMutableCollection(Type type)
    {
        if (type == typeof(string))
        {
            return false;
        }

        if (type.IsArray ||
            typeof(IList).IsAssignableFrom(type) ||
            typeof(IDictionary).IsAssignableFrom(type))
        {
            return true;
        }

        var genericTypes = type
            .GetInterfaces()
            .Append(type)
            .Where(candidate => candidate.IsGenericType)
            .Select(candidate =>
                candidate.GetGenericTypeDefinition())
            .ToArray();

        if (genericTypes.Contains(typeof(ICollection<>)) ||
            genericTypes.Contains(typeof(IList<>)) ||
            genericTypes.Contains(typeof(IDictionary<,>)) ||
            genericTypes.Contains(typeof(ISet<>)))
        {
            return true;
        }

        return type.IsGenericType &&
               type.GetGenericArguments()
                   .Any(ContainsMutableCollection);
    }

    private static IEnumerable<Type> GetContainedTypes(Type type)
    {
        yield return type;

        var nullableType = Nullable.GetUnderlyingType(type);

        if (nullableType is not null)
        {
            foreach (var containedType in GetContainedTypes(nullableType))
            {
                yield return containedType;
            }
        }

        if (type.IsArray && type.GetElementType() is { } elementType)
        {
            foreach (var containedType in GetContainedTypes(elementType))
            {
                yield return containedType;
            }
        }

        if (!type.IsGenericType)
        {
            yield break;
        }

        foreach (var genericArgument in type.GetGenericArguments())
        {
            foreach (var containedType in GetContainedTypes(genericArgument))
            {
                yield return containedType;
            }
        }
    }

    private sealed record PublicStateMember(
        Type DeclaringType,
        string Name,
        Type MemberType);
}
