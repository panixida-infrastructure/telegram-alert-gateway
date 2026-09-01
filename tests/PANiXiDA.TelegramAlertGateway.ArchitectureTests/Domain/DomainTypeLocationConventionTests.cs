using PANiXiDA.Core.Domain;
using PANiXiDA.Core.Domain.AggregateRoots;
using PANiXiDA.Core.Domain.DomainEvents;
using PANiXiDA.Core.Domain.Entities;
using PANiXiDA.Core.Domain.Identifiers;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Domain;

public sealed class DomainTypeLocationConventionTests
{
    private const string DomainAssemblySuffix = ".Domain";
    private const string EntitiesDirectoryName = "Entities";
    private const string EnumerationsDirectoryName = "Enumerations";
    private const string EventsDirectoryName = "Events";
    private const string SourceDirectoryName = "src";
    private const string ValueObjectsDirectoryName = "ValueObjects";

    [Fact(DisplayName = "Aggregate roots should have singular names and plural directories when declared")]
    public void AggregateRoots_Should_HaveSingularNamesAndPluralDirectories_When_Declared()
    {
        var repositoryRoot = FindRepositoryRoot();
        var aggregateRoots = GetDomainTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false } &&
                typeof(IAggregateRoot).IsAssignableFrom(type))
            .ToArray();
        var violations = aggregateRoots
            .SelectMany(type =>
            {
                var assemblyName = GetAssemblyName(type);
                var expectedNamespace =
                    $"{assemblyName}." +
                    $"{EnglishNamingConvention.Pluralize(type.Name)}";

                return GetLocationViolations(
                    repositoryRoot,
                    type,
                    expectedNamespace,
                    $"Aggregate root '{type.FullName}' must have a singular " +
                    $"type name and reside in plural directory " +
                    $"'{EnglishNamingConvention.Pluralize(type.Name)}'.");
            })
            .ToArray();

        Assert.NotEmpty(aggregateRoots);
        Assert.True(
            violations.Length == 0,
            $"Aggregate root location violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Entities should have singular names and plural directories when declared")]
    public void Entities_Should_HaveSingularNamesAndPluralDirectories_When_Declared()
    {
        var repositoryRoot = FindRepositoryRoot();
        var owners = GetDomainOwners();
        var entities = owners
            .Where(type =>
                typeof(IEntity).IsAssignableFrom(type) &&
                !typeof(IAggregateRoot).IsAssignableFrom(type))
            .ToArray();
        var violations = entities
            .SelectMany(entity => GetEntityLocationViolations(
                repositoryRoot,
                entity,
                owners))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Entity location violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Value objects should reside in owner ValueObjects directories when declared")]
    public void ValueObjects_Should_ResideInOwnerValueObjectsDirectories_When_Declared()
    {
        var repositoryRoot = FindRepositoryRoot();
        var owners = GetDomainOwners();
        var valueObjects = GetDomainTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false } &&
                typeof(ValueObject).IsAssignableFrom(type))
            .ToArray();
        var violations = valueObjects
            .SelectMany(valueObject => GetOwnedTypeLocationViolations(
                repositoryRoot,
                valueObject,
                owners,
                ValueObjectsDirectoryName))
            .ToArray();

        Assert.NotEmpty(valueObjects);
        Assert.True(
            violations.Length == 0,
            $"Value object location violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Enumerations should reside in owner Enumerations directories when declared")]
    public void Enumerations_Should_ResideInOwnerEnumerationsDirectories_When_Declared()
    {
        var repositoryRoot = FindRepositoryRoot();
        var owners = GetDomainOwners();
        var enumerations = GetDomainTypes()
            .Where(IsEnumeration)
            .ToArray();
        var violations = enumerations
            .SelectMany(enumeration => GetOwnedTypeLocationViolations(
                repositoryRoot,
                enumeration,
                owners,
                EnumerationsDirectoryName))
            .ToArray();

        Assert.NotEmpty(enumerations);
        Assert.True(
            violations.Length == 0,
            $"Enumeration location violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Domain events should reside in Events directories when declared")]
    public void DomainEvents_Should_ResideInEventsDirectories_When_Declared()
    {
        var repositoryRoot = FindRepositoryRoot();
        var domainEvents = GetDomainTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false } &&
                typeof(DomainEvent).IsAssignableFrom(type))
            .ToArray();
        var violations = domainEvents
            .SelectMany(domainEvent =>
            {
                var domainEventNamespace = domainEvent.Namespace;

                if (domainEventNamespace is null ||
                    !domainEventNamespace.EndsWith(
                        $".{EventsDirectoryName}",
                        StringComparison.Ordinal))
                {
                    return
                    [
                        $"{domainEvent.FullName} must reside in an " +
                        $"'{EventsDirectoryName}' namespace and directory."
                    ];
                }

                return GetSourceFileViolations(
                    repositoryRoot,
                    domainEvent,
                    domainEventNamespace);
            })
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Domain event location violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Strongly typed ids should match owner names and locations when declared")]
    public void StronglyTypedIds_Should_MatchOwnerNamesAndLocations_When_Declared()
    {
        var repositoryRoot = FindRepositoryRoot();
        var owners = GetDomainOwners();
        var stronglyTypedIds = GetDomainTypes()
            .Where(type =>
                !type.IsInterface &&
                typeof(IStronglyTypedId).IsAssignableFrom(type))
            .ToArray();
        var ownerIdentifiers = owners
            .Select(owner => new
            {
                Owner = owner,
                Identifier = GetIdentifierType(owner)
            })
            .ToArray();
        var violations = ownerIdentifiers
            .SelectMany(target => GetIdentifierViolations(
                repositoryRoot,
                target.Owner,
                target.Identifier))
            .Concat(stronglyTypedIds
                .Where(identifier => !ownerIdentifiers.Any(target =>
                    target.Identifier == identifier))
                .Select(identifier =>
                    $"{identifier.FullName} must be used as the identifier " +
                    $"of an aggregate root or entity."))
            .ToArray();

        Assert.NotEmpty(owners);
        Assert.NotEmpty(stronglyTypedIds);
        Assert.True(
            violations.Length == 0,
            $"Strongly typed id convention violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static Type[] GetDomainTypes()
    {
        return
        [
            .. ArchitectureDefinition.ProductionAssemblies
                .Where(assembly => assembly.GetName().Name?.EndsWith(
                    DomainAssemblySuffix,
                    StringComparison.Ordinal) == true)
                .SelectMany(assembly => assembly.GetTypes())
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
        ];
    }

    private static Type[] GetDomainOwners()
    {
        return
        [
            .. GetDomainTypes()
                .Where(type =>
                    type is { IsClass: true, IsAbstract: false } &&
                    typeof(IEntity).IsAssignableFrom(type))
        ];
    }

    private static List<string> GetEntityLocationViolations(
        string repositoryRoot,
        Type entity,
        IReadOnlyCollection<Type> owners)
    {
        var expectedNamespaceSuffix =
            $".{EntitiesDirectoryName}." +
            $"{EnglishNamingConvention.Pluralize(entity.Name)}";
        var entityNamespace = entity.Namespace;

        if (entityNamespace is null ||
            !entityNamespace.EndsWith(
                expectedNamespaceSuffix,
                StringComparison.Ordinal))
        {
            return
            [
                $"{entity.FullName} must have a singular type name and " +
                $"reside in " +
                $"'<owner>.{EntitiesDirectoryName}." +
                $"{EnglishNamingConvention.Pluralize(entity.Name)}'."
            ];
        }

        var ownerNamespace =
            entityNamespace[..^expectedNamespaceSuffix.Length];
        var hasOwner = owners.Any(owner =>
            owner != entity &&
            string.Equals(
                owner.Namespace,
                ownerNamespace,
                StringComparison.Ordinal));
        var violations = new List<string>();

        if (!hasOwner)
        {
            violations.Add(
                $"{entity.FullName} must reside under an aggregate root or " +
                $"entity namespace, found owner namespace " +
                $"'{ownerNamespace}'.");
        }

        violations.AddRange(GetSourceFileViolations(
            repositoryRoot,
            entity,
            entityNamespace));

        return violations;
    }

    private static List<string> GetOwnedTypeLocationViolations(
        string repositoryRoot,
        Type ownedType,
        IReadOnlyCollection<Type> owners,
        string categoryDirectoryName)
    {
        var expectedNamespaceSuffix = $".{categoryDirectoryName}";
        var ownedTypeNamespace = ownedType.Namespace;

        if (ownedTypeNamespace is null ||
            !ownedTypeNamespace.EndsWith(
                expectedNamespaceSuffix,
                StringComparison.Ordinal))
        {
            return
            [
                $"{ownedType.FullName} must reside in a " +
                $"'{categoryDirectoryName}' namespace and directory under " +
                $"its aggregate root or entity."
            ];
        }

        var ownerNamespace =
            ownedTypeNamespace[..^expectedNamespaceSuffix.Length];
        var hasOwner = owners.Any(owner => string.Equals(
            owner.Namespace,
            ownerNamespace,
            StringComparison.Ordinal));
        var violations = new List<string>();

        if (!hasOwner)
        {
            violations.Add(
                $"{ownedType.FullName} must belong to an aggregate root or " +
                $"entity in namespace '{ownerNamespace}'.");
        }

        violations.AddRange(GetSourceFileViolations(
            repositoryRoot,
            ownedType,
            ownedTypeNamespace));

        return violations;
    }

    private static List<string> GetLocationViolations(
        string repositoryRoot,
        Type type,
        string expectedNamespace,
        string namespaceViolation)
    {
        var violations = new List<string>();

        if (!string.Equals(
                type.Namespace,
                expectedNamespace,
                StringComparison.Ordinal))
        {
            violations.Add(
                $"{namespaceViolation} Expected namespace " +
                $"'{expectedNamespace}', found '{type.Namespace}'.");
        }

        violations.AddRange(GetSourceFileViolations(
            repositoryRoot,
            type,
            expectedNamespace));

        return violations;
    }

    private static List<string> GetIdentifierViolations(
        string repositoryRoot,
        Type owner,
        Type identifier)
    {
        var expectedIdentifierName = owner.Name + "Id";
        var violations = new List<string>();

        if (!string.Equals(
                identifier.Name,
                expectedIdentifierName,
                StringComparison.Ordinal))
        {
            violations.Add(
                $"{owner.FullName} identifier must be named " +
                $"'{expectedIdentifierName}', found '{identifier.Name}'.");
        }

        if (!typeof(IStronglyTypedId).IsAssignableFrom(identifier))
        {
            violations.Add(
                $"{owner.FullName} identifier '{identifier.FullName}' must " +
                $"implement IStronglyTypedId.");
        }

        if (!string.Equals(
                identifier.Namespace,
                owner.Namespace,
                StringComparison.Ordinal))
        {
            violations.Add(
                $"{identifier.FullName} must reside next to its owner " +
                $"'{owner.FullName}' in namespace '{owner.Namespace}'.");
        }

        violations.AddRange(GetSourceFileViolations(
            repositoryRoot,
            identifier,
            owner.Namespace
                ?? throw new InvalidOperationException(
                    $"Domain owner '{owner.FullName}' has no namespace.")));

        return violations;
    }

    private static IEnumerable<string> GetSourceFileViolations(
        string repositoryRoot,
        Type type,
        string expectedNamespace)
    {
        var expectedPath = GetExpectedSourceFilePath(
            repositoryRoot,
            type,
            expectedNamespace);

        return File.Exists(expectedPath)
            ? []
            :
            [
                $"{type.FullName} must be declared at " +
                $"'{Path.GetRelativePath(repositoryRoot, expectedPath)}'."
            ];
    }

    private static string GetExpectedSourceFilePath(
        string repositoryRoot,
        Type type,
        string expectedNamespace)
    {
        var assemblyName = GetAssemblyName(type);
        var module = ArchitectureDefinition.Modules.Single(candidate =>
            candidate.DomainAssemblyName == assemblyName);
        var moduleDirectoryName =
            module.Name[(module.Name.LastIndexOf('.') + 1)..];
        var namespacePrefix = assemblyName + ".";
        var relativeNamespace = expectedNamespace.StartsWith(
            namespacePrefix,
            StringComparison.Ordinal)
            ? expectedNamespace[namespacePrefix.Length..]
            : throw new InvalidOperationException(
                $"Expected namespace '{expectedNamespace}' must start with " +
                $"'{namespacePrefix}'.");

        return Path.Combine(
            repositoryRoot,
            SourceDirectoryName,
            moduleDirectoryName,
            assemblyName,
            relativeNamespace.Replace(
                '.',
                Path.DirectorySeparatorChar),
            $"{type.Name.Split('`')[0]}.cs");
    }

    private static Type GetIdentifierType(Type owner)
    {
        for (var currentType = owner;
             currentType is not null;
             currentType = currentType.BaseType)
        {
            if (!currentType.IsGenericType)
            {
                continue;
            }

            var genericTypeDefinition =
                currentType.GetGenericTypeDefinition();

            if (genericTypeDefinition == typeof(AggregateRoot<>) ||
                genericTypeDefinition == typeof(Entity<>))
            {
                return currentType.GetGenericArguments()[0];
            }
        }

        throw new InvalidOperationException(
            $"Could not determine identifier type for '{owner.FullName}'.");
    }

    private static bool IsEnumeration(Type type)
    {
        for (var currentType = type;
             currentType is not null;
             currentType = currentType.BaseType)
        {
            if (currentType.IsGenericType &&
                currentType.GetGenericTypeDefinition() ==
                typeof(Enumeration<>))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetAssemblyName(Type type)
    {
        return type.Assembly.GetName().Name
            ?? throw new InvalidOperationException(
                $"Could not determine assembly for '{type.FullName}'.");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(
                    directory.FullName,
                    SourceDirectoryName)))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find repository root containing " +
            $"the '{SourceDirectoryName}' directory.");
    }
}
