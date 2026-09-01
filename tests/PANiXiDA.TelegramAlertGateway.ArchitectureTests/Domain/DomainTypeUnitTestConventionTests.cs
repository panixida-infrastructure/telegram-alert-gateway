using System.Reflection;
using System.Runtime.CompilerServices;

using PANiXiDA.Core.Domain;
using PANiXiDA.Core.Domain.AggregateRoots;
using PANiXiDA.Core.Domain.Entities;
using PANiXiDA.Core.Domain.Identifiers;

using PANiXiDA.TelegramAlertGateway.ArchitectureTests.Tests;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Domain;

public sealed class DomainTypeUnitTestConventionTests
{
    private const string DomainDirectoryName = "Domain";
    private const string SourceDirectoryName = "src";
    private const string TestsDirectoryName = "tests";
    private const string UnitTestsAssemblySuffix = ".UnitTests";

    [Fact(DisplayName = "Domain types should have matching unit test files when domain types are declared")]
    public void DomainTypes_Should_HaveMatchingUnitTestFiles_When_DomainTypesAreDeclared()
    {
        var repositoryRoot = FindRepositoryRoot();
        var domainTypes = GetDomainTypes(repositoryRoot);
        var testMethods = TestSourceDiscovery.GetTestMethods();

        Assert.NotEmpty(domainTypes);

        var violations = domainTypes
            .Select(domainType => new
            {
                DomainType = domainType,
                TestMethods = testMethods
                    .Where(testMethod => HasPath(
                        repositoryRoot,
                        testMethod,
                        domainType.ExpectedTestFilePath))
                    .ToArray()
            })
            .Where(target =>
                !File.Exists(target.DomainType.ExpectedTestFilePath) ||
                target.TestMethods.Length == 0)
            .Select(target =>
                $"{target.DomainType.Type.FullName} must have a separate unit " +
                $"test file containing at least one Fact or Theory at " +
                $"'{Path.GetRelativePath(
                    repositoryRoot,
                    target.DomainType.ExpectedTestFilePath)}'.")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Missing or empty Domain unit test files:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Domain unit tests should cover every accessible method when domain methods are declared")]
    public void DomainUnitTests_Should_CoverEveryAccessibleMethod_When_DomainMethodsAreDeclared()
    {
        var repositoryRoot = FindRepositoryRoot();
        var domainTypes = GetDomainTypes(repositoryRoot);
        var testMethods = TestSourceDiscovery.GetTestMethods();

        Assert.NotEmpty(domainTypes);

        var violations = domainTypes
            .Where(domainType =>
                File.Exists(domainType.ExpectedTestFilePath))
            .SelectMany(domainType => GetMissingTestMethods(
                repositoryRoot,
                domainType,
                testMethods))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Missing Domain unit test methods:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static DomainType[] GetDomainTypes(string repositoryRoot)
    {
        var productionAssemblies = ArchitectureDefinition.ProductionAssemblies
            .ToDictionary(
                assembly => assembly.GetName().Name
                    ?? throw new InvalidOperationException(
                        $"Could not determine the name of assembly '{assembly.FullName}'."),
                StringComparer.Ordinal);

        return
        [
            .. ArchitectureDefinition.Modules
                .SelectMany(module => GetDomainTypes(
                    repositoryRoot,
                    module,
                    productionAssemblies[module.DomainAssemblyName]))
                .OrderBy(domainType => domainType.Type.FullName, StringComparer.Ordinal)
        ];
    }

    private static IEnumerable<DomainType> GetDomainTypes(
        string repositoryRoot,
        ModuleArchitecture module,
        Assembly domainAssembly)
    {
        var moduleDirectoryName =
            module.Name[(module.Name.LastIndexOf('.') + 1)..];
        var unitTestsAssemblyName =
            module.Name + UnitTestsAssemblySuffix;

        return domainAssembly
            .GetTypes()
            .Where(IsDomainType)
            .Select(type =>
            {
                var relativeNamespace = GetRelativeNamespace(
                    type,
                    module.DomainAssemblyName);
                var namespacePath = relativeNamespace.Replace(
                    '.',
                    Path.DirectorySeparatorChar);
                var typeName = type.Name.Split('`')[0];

                return new DomainType(
                    Type: type,
                    ExpectedTestFilePath: Path.Combine(
                        repositoryRoot,
                        TestsDirectoryName,
                        moduleDirectoryName,
                        unitTestsAssemblyName,
                        DomainDirectoryName,
                        namespacePath,
                        $"{typeName}Tests.cs"));
            });
    }

    private static bool IsDomainType(Type type)
    {
        return !type.IsAbstract &&
               (typeof(IAggregateRoot).IsAssignableFrom(type) ||
                typeof(IEntity).IsAssignableFrom(type) ||
                typeof(ValueObject).IsAssignableFrom(type) ||
                typeof(IStronglyTypedId).IsAssignableFrom(type) ||
                IsEnumeration(type));
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

    private static string GetRelativeNamespace(
        Type type,
        string domainAssemblyName)
    {
        var typeNamespace = type.Namespace
            ?? throw new InvalidOperationException(
                $"Domain type '{type.FullName}' does not have a namespace.");
        var domainNamespacePrefix = domainAssemblyName + ".";

        if (string.Equals(
                typeNamespace,
                domainAssemblyName,
                StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (typeNamespace.StartsWith(
                domainNamespacePrefix,
                StringComparison.Ordinal))
        {
            return typeNamespace[domainNamespacePrefix.Length..];
        }

        throw new InvalidOperationException(
            $"Domain type namespace '{typeNamespace}' must be " +
            $"'{domainAssemblyName}' or start with " +
            $"'{domainNamespacePrefix}'.");
    }

    private static IEnumerable<string> GetMissingTestMethods(
        string repositoryRoot,
        DomainType domainType,
        IReadOnlyCollection<TestMethodSource> testMethods)
    {
        var testMethodNames = testMethods
            .Where(testMethod => HasPath(
                repositoryRoot,
                testMethod,
                domainType.ExpectedTestFilePath))
            .Select(testMethod => testMethod.Name)
            .ToArray();
        var domainMethods = domainType.Type
            .GetMethods(
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly)
            .Where(IsAccessibleDomainMethod)
            .GroupBy(method => method.Name, StringComparer.Ordinal);

        return domainMethods
            .Select(methods => new
            {
                MethodName = methods.Key,
                RequiredTestCount = methods.Count(),
                ActualTestCount = testMethodNames.Count(testMethodName =>
                    testMethodName.StartsWith(
                        methods.Key + "_Should_",
                        StringComparison.Ordinal))
            })
            .Where(coverage =>
                coverage.ActualTestCount < coverage.RequiredTestCount)
            .Select(coverage =>
                $"{domainType.Type.FullName}.{coverage.MethodName}: expected " +
                $"{coverage.RequiredTestCount} test method(s) starting with " +
                $"'{coverage.MethodName}_Should_', found " +
                $"{coverage.ActualTestCount} in " +
                $"'{Path.GetRelativePath(
                    repositoryRoot,
                    domainType.ExpectedTestFilePath)}'.");
    }

    private static bool IsAccessibleDomainMethod(MethodInfo method)
    {
        return !method.IsSpecialName &&
               (method.IsPublic ||
                method.IsAssembly ||
                method.IsFamilyOrAssembly) &&
               method.GetCustomAttribute<CompilerGeneratedAttribute>() is null;
    }

    private static bool HasPath(
        string repositoryRoot,
        TestMethodSource testMethod,
        string expectedPath)
    {
        var testMethodPath = Path.GetFullPath(
            Path.Combine(
                repositoryRoot,
                testMethod.RelativePath));

        return string.Equals(
            testMethodPath,
            expectedPath,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(
                    directory.FullName,
                    SourceDirectoryName)) &&
                Directory.Exists(Path.Combine(
                    directory.FullName,
                    TestsDirectoryName)))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find repository root containing " +
            $"'{SourceDirectoryName}' and '{TestsDirectoryName}' directories.");
    }

    private sealed record DomainType(
        Type Type,
        string ExpectedTestFilePath);
}
