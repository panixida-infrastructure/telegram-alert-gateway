using System.Reflection;

using PANiXiDA.Core.Domain;
using PANiXiDA.Core.Domain.Abstractions;
using PANiXiDA.Core.Domain.AggregateRoots;
using PANiXiDA.Core.Domain.Identifiers;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Domain;

public sealed class RepositoryConventionTests
{
    private const string AbstractionsNamespaceSegment = "Abstractions";
    private const string ApplicationAssemblySuffix = ".Application";
    private const string DomainAssemblySuffix = ".Domain";
    private const string RepositorySuffix = "Repository";
    private const string SourceDirectoryName = "src";

    [Fact(DisplayName = "Repositories should reside in Domain abstractions when declared")]
    public void Repositories_Should_ResideInDomainAbstractions_When_Declared()
    {
        var repositoryRoot = FindRepositoryRoot();
        var repositories = GetRepositories();
        var violations = repositories
            .SelectMany(repository => GetLocationViolations(
                repositoryRoot,
                repository.Type))
            .ToArray();

        Assert.NotEmpty(repositories);
        Assert.True(
            violations.Length == 0,
            $"Repository location violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Repositories should use strongly typed ids and aggregate roots when declared")]
    public void Repositories_Should_UseStronglyTypedIdsAndAggregateRoots_When_Declared()
    {
        var repositories = GetRepositories();
        var violations = repositories
            .SelectMany(GetContractBoundaryViolations)
            .ToArray();

        Assert.NotEmpty(repositories);
        Assert.True(
            violations.Length == 0,
            $"Repository boundary type violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Repository methods should use only domain types when declared")]
    public void RepositoryMethods_Should_UseOnlyDomainTypes_When_Declared()
    {
        var methods = GetRepositoryMethods();
        var violations = methods
            .Where(target =>
                !IsAllowedRepositoryMethodType(
                    target.Method.ReturnType,
                    new HashSet<Type>()) ||
                target.Method.GetParameters().Any(parameter =>
                    !IsAllowedRepositoryMethodType(
                        parameter.ParameterType,
                        new HashSet<Type>())))
            .Select(target =>
                $"{target.Repository.FullName}.{target.Method.Name} may use " +
                $"only aggregate roots, value objects, Enumerations, " +
                $"strongly typed IDs, CancellationToken, and framework " +
                $"wrappers; found signature '{target.Method}'.")
            .ToArray();

        Assert.NotEmpty(methods);
        Assert.True(
            violations.Length == 0,
            $"Repository method type violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Repositories should match plural aggregate names when declared")]
    public void Repositories_Should_MatchPluralAggregateNames_When_Declared()
    {
        var repositories = GetRepositories();
        var violations = repositories
            .Select(repository => new
            {
                repository.Type,
                ActualFeatureName = GetFeatureName(repository.Type),
                ExpectedFeatureName = EnglishNamingConvention.Pluralize(
                    repository.Contract.GetGenericArguments()[1].Name)
            })
            .Select(repository => new
            {
                repository.Type,
                repository.ActualFeatureName,
                repository.ExpectedFeatureName,
                ExpectedName =
                    "I" + repository.ExpectedFeatureName + RepositorySuffix
            })
            .Where(repository =>
                !string.Equals(
                    repository.Type.Name,
                    repository.ExpectedName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    repository.ActualFeatureName,
                    repository.ExpectedFeatureName,
                    StringComparison.Ordinal))
            .Select(repository =>
                $"{repository.Type.FullName} must be named " +
                $"'{repository.ExpectedName}' and reside under aggregate " +
                $"feature '{repository.ExpectedFeatureName}'.")
            .ToArray();

        Assert.NotEmpty(repositories);
        Assert.True(
            violations.Length == 0,
            $"Repository interface naming violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Repository constructor parameters should follow type-based naming when repository is injected")]
    public void ConstructorParameters_Should_FollowTypeBasedNaming_When_RepositoryIsInjected()
    {
        var repositoryParameters = ArchitectureDefinition.ProductionAssemblies
            .Where(assembly => assembly.GetName().Name?.EndsWith(
                ApplicationAssemblySuffix,
                StringComparison.Ordinal) == true)
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type
                .GetConstructors(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic)
                .SelectMany(constructor => constructor
                    .GetParameters()
                    .Where(parameter => GetClosedRepositoryContract(
                        parameter.ParameterType) is not null)
                    .Select(parameter => new ConstructorParameter(
                        type,
                        parameter))))
            .OrderBy(candidate => candidate.DeclaringType.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(repositoryParameters);

        var violations = repositoryParameters
            .Select(candidate => new
            {
                candidate.DeclaringType,
                candidate.Parameter,
                ExpectedName = GetExpectedParameterName(
                    candidate.Parameter.ParameterType)
            })
            .Where(candidate => !string.Equals(
                candidate.Parameter.Name,
                candidate.ExpectedName,
                StringComparison.Ordinal))
            .Select(candidate =>
                $"{candidate.DeclaringType.FullName} constructor parameter " +
                $"'{candidate.Parameter.Name}' of type " +
                $"'{candidate.Parameter.ParameterType.FullName}' must be named " +
                $"'{candidate.ExpectedName}'.")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            string.Join(Environment.NewLine, violations));
    }

    private static Repository[] GetRepositories()
    {
        return
        [
            .. ArchitectureDefinition.ProductionAssemblies
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.IsInterface)
                .Select(type => new
                {
                    Type = type,
                    Contract = GetClosedRepositoryContract(type)
                })
                .Where(repository => repository.Contract is not null)
                .Select(repository => new Repository(
                    repository.Type,
                    repository.Contract!))
                .OrderBy(
                    repository => repository.Type.FullName,
                    StringComparer.Ordinal)
        ];
    }

    private static List<string> GetLocationViolations(
        string repositoryRoot,
        Type repository)
    {
        var violations = new List<string>();
        var assemblyName = GetAssemblyName(repository);

        if (!assemblyName.EndsWith(
                DomainAssemblySuffix,
                StringComparison.Ordinal))
        {
            violations.Add(
                $"{repository.FullName} inherits IRepository<,> and must be " +
                $"declared in Domain.");
        }

        if (!HasNamespaceSegment(
                repository,
                AbstractionsNamespaceSegment))
        {
            violations.Add(
                $"{repository.FullName} must be declared in an " +
                $"'{AbstractionsNamespaceSegment}' namespace.");
        }

        var expectedSourceFilePath = GetExpectedSourceFilePath(
            repositoryRoot,
            repository);

        if (!File.Exists(expectedSourceFilePath))
        {
            violations.Add(
                $"{repository.FullName} must have a matching source file at " +
                $"'{Path.GetRelativePath(
                    repositoryRoot,
                    expectedSourceFilePath)}'.");
        }

        return violations;
    }

    private static IEnumerable<string> GetContractBoundaryViolations(
        Repository repository)
    {
        var genericArguments = repository.Contract.GetGenericArguments();
        var identifierType = genericArguments[0];
        var aggregateType = genericArguments[1];
        var violations = new List<string>();

        if (IsPrimitiveValue(identifierType) ||
            !typeof(IStronglyTypedId).IsAssignableFrom(identifierType))
        {
            violations.Add(
                $"{repository.Type.FullName} must use a non-primitive " +
                $"strongly typed ID, found '{identifierType.FullName}'.");
        }

        if (IsPrimitiveValue(aggregateType) ||
            !typeof(IAggregateRoot).IsAssignableFrom(aggregateType))
        {
            violations.Add(
                $"{repository.Type.FullName} must use an aggregate root, " +
                $"found '{aggregateType.FullName}'.");
        }

        return violations;
    }

    private static RepositoryMethod[] GetRepositoryMethods()
    {
        return
        [
            .. GetRepositories()
                .SelectMany(repository => repository.Type
                    .GetMethods(
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.DeclaredOnly)
                    .Concat(repository.Contract.GetMethods())
                    .DistinctBy(method => method.ToString())
                    .Select(method => new RepositoryMethod(
                        repository.Type,
                        method)))
                .OrderBy(
                    target => target.Repository.FullName,
                    StringComparer.Ordinal)
                .ThenBy(
                    target => target.Method.Name,
                    StringComparer.Ordinal)
        ];
    }

    private static Type? GetClosedRepositoryContract(Type type)
    {
        return type
            .GetInterfaces()
            .Append(type)
            .FirstOrDefault(candidate =>
                candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() ==
                typeof(IRepository<,>));
    }

    private static bool IsAllowedRepositoryMethodType(
        Type type,
        ISet<Type> visitedTypes)
    {
        if (!visitedTypes.Add(type))
        {
            return true;
        }

        if (typeof(IAggregateRoot).IsAssignableFrom(type) ||
            typeof(ValueObject).IsAssignableFrom(type) ||
            typeof(IStronglyTypedId).IsAssignableFrom(type) ||
            IsEnumeration(type))
        {
            return true;
        }

        if (type == typeof(void) ||
            type == typeof(Task) ||
            type == typeof(ValueTask) ||
            type == typeof(CancellationToken))
        {
            return true;
        }

        var nullableType = Nullable.GetUnderlyingType(type);

        if (nullableType is not null)
        {
            return IsAllowedRepositoryMethodType(
                nullableType,
                visitedTypes);
        }

        if (type.HasElementType)
        {
            return IsAllowedRepositoryMethodType(
                type.GetElementType()
                    ?? throw new InvalidOperationException(
                        $"Could not determine element type for '{type}'."),
                visitedTypes);
        }

        var collectionElementType = type
            .GetInterfaces()
            .Append(type)
            .Where(candidate => candidate.IsGenericType)
            .FirstOrDefault(candidate =>
                candidate.GetGenericTypeDefinition() ==
                typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];

        if (collectionElementType is not null)
        {
            return IsAllowedRepositoryMethodType(
                collectionElementType,
                visitedTypes);
        }

        if (!type.IsGenericType)
        {
            return false;
        }

        var genericTypeDefinition = type.GetGenericTypeDefinition();

        return (genericTypeDefinition == typeof(Task<>) ||
                genericTypeDefinition == typeof(ValueTask<>)) &&
               type.GetGenericArguments().All(argument =>
                   IsAllowedRepositoryMethodType(
                       argument,
                       visitedTypes));
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

    private static bool IsPrimitiveValue(Type type)
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

    private static string GetFeatureName(Type repository)
    {
        var repositoryNamespace = repository.Namespace
            ?? throw new InvalidOperationException(
                $"Repository interface '{repository.FullName}' does not have " +
                $"a namespace.");
        var namespaceSegments = repositoryNamespace.Split('.');
        var abstractionsIndex = Array.LastIndexOf(
            namespaceSegments,
            AbstractionsNamespaceSegment);

        if (abstractionsIndex < 1)
        {
            throw new InvalidOperationException(
                $"Repository interface '{repository.FullName}' must be " +
                $"declared in an '*.{{Feature}}." +
                $"{AbstractionsNamespaceSegment}' namespace.");
        }

        return namespaceSegments[abstractionsIndex - 1];
    }

    private static string GetExpectedParameterName(Type parameterType)
    {
        var typeName = RemoveGenericArity(parameterType.Name);
        var nameWithoutInterfacePrefix = typeName[1..];

        return char.ToLowerInvariant(nameWithoutInterfacePrefix[0]) +
               nameWithoutInterfacePrefix[1..];
    }

    private static string RemoveGenericArity(string typeName)
    {
        var genericAritySeparatorIndex =
            typeName.IndexOf('`', StringComparison.Ordinal);

        return genericAritySeparatorIndex < 0
            ? typeName
            : typeName[..genericAritySeparatorIndex];
    }

    private static bool HasNamespaceSegment(
        Type type,
        string namespaceSegment)
    {
        return type.Namespace?
            .Split('.')
            .Contains(namespaceSegment, StringComparer.Ordinal) == true;
    }

    private static string GetExpectedSourceFilePath(
        string repositoryRoot,
        Type repository)
    {
        var assemblyName = GetAssemblyName(repository);
        var module = ArchitectureDefinition.Modules.Single(candidate =>
            string.Equals(
                candidate.ContractsAssemblyName,
                assemblyName,
                StringComparison.Ordinal) ||
            string.Equals(
                candidate.DomainAssemblyName,
                assemblyName,
                StringComparison.Ordinal) ||
            string.Equals(
                candidate.ApplicationAssemblyName,
                assemblyName,
                StringComparison.Ordinal) ||
            string.Equals(
                candidate.InfrastructureAssemblyName,
                assemblyName,
                StringComparison.Ordinal) ||
            string.Equals(
                candidate.PresentationAssemblyName,
                assemblyName,
                StringComparison.Ordinal));
        var moduleDirectoryName =
            module.Name[(module.Name.LastIndexOf('.') + 1)..];
        var repositoryNamespace = repository.Namespace
            ?? throw new InvalidOperationException(
                $"Repository interface '{repository.FullName}' does not have " +
                $"a namespace.");
        var namespacePrefix = assemblyName + ".";
        var relativeNamespace = repositoryNamespace.StartsWith(
            namespacePrefix,
            StringComparison.Ordinal)
            ? repositoryNamespace[namespacePrefix.Length..]
            : throw new InvalidOperationException(
                $"Repository interface '{repository.FullName}' namespace must " +
                $"start with '{namespacePrefix}'.");

        return Path.Combine(
            repositoryRoot,
            SourceDirectoryName,
            moduleDirectoryName,
            assemblyName,
            relativeNamespace.Replace(
                '.',
                Path.DirectorySeparatorChar),
            $"{repository.Name.Split('`')[0]}.cs");
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

    private sealed record Repository(
        Type Type,
        Type Contract);

    private sealed record RepositoryMethod(
        Type Repository,
        MethodInfo Method);

    private sealed record ConstructorParameter(
        Type DeclaringType,
        ParameterInfo Parameter);
}
