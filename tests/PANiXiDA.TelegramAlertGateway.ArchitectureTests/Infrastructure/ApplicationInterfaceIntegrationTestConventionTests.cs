using System.Reflection;
using System.Text.RegularExpressions;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Infrastructure;

public sealed partial class ApplicationInterfaceIntegrationTestConventionTests
{
    private const string IntegrationTestsAssemblySuffix = ".IntegrationTests";
    private const string InfrastructureDirectoryName = "Infrastructure";
    private const string SourceDirectoryName = "src";
    private const string TestsDirectoryName = "tests";

    [GeneratedRegex(
        @"\[(?:Fact|Theory)(?:Attribute)?(?:\([^\]]*\))?\]" +
        @"(?:\s*\[[^\]]+\])*\s*" +
        @"public\s+(?:async\s+)?" +
        @"(?:void|(?:Task|ValueTask)(?:<[^>]+>)?)\s+" +
        @"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex TestMethodPattern();

    [Fact(DisplayName = "Infrastructure implementations of Application interfaces should have matching integration test files when application interfaces are implemented")]
    public void InfrastructureImplementations_Should_HaveMatchingIntegrationTestFiles_When_ApplicationInterfacesAreImplemented()
    {
        var repositoryRoot = FindRepositoryRoot();
        var implementations = GetInfrastructureImplementations();

        Assert.NotEmpty(implementations);

        var missingTestFiles = implementations
            .Select(target => new
            {
                target.Implementation,
                ExpectedPath = GetExpectedTestFilePath(
                    repositoryRoot,
                    target.Module,
                    target.Implementation)
            })
            .Where(target => !File.Exists(target.ExpectedPath))
            .Select(target =>
                $"{target.Implementation.FullName}: " +
                Path.GetRelativePath(repositoryRoot, target.ExpectedPath))
            .ToArray();

        Assert.True(
            missingTestFiles.Length == 0,
            $"Missing integration test files:{Environment.NewLine}" +
            string.Join(Environment.NewLine, missingTestFiles));
    }

    [Fact(DisplayName = "Integration tests should cover every implemented Application interface method when implementation exists")]
    public void IntegrationTests_Should_CoverEveryApplicationInterfaceMethod_When_ImplementationExists()
    {
        var repositoryRoot = FindRepositoryRoot();
        var implementations = GetInfrastructureImplementations();

        Assert.NotEmpty(implementations);

        var missingTestMethods = implementations
            .SelectMany(target => GetMissingTestMethods(repositoryRoot, target))
            .ToArray();

        Assert.True(
            missingTestMethods.Length == 0,
            $"Missing integration test methods:{Environment.NewLine}" +
            string.Join(Environment.NewLine, missingTestMethods));
    }

    private static InfrastructureImplementation[] GetInfrastructureImplementations()
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
                .SelectMany(module => GetInfrastructureImplementations(
                    module,
                    productionAssemblies))
                .OrderBy(target => target.Implementation.FullName, StringComparer.Ordinal)
        ];
    }

    private static IEnumerable<InfrastructureImplementation> GetInfrastructureImplementations(
        ModuleArchitecture module,
        Dictionary<string, Assembly> productionAssemblies)
    {
        var applicationAssembly = productionAssemblies[module.ApplicationAssemblyName];
        var infrastructureAssembly = productionAssemblies[module.InfrastructureAssemblyName];
        var applicationInterfaces = applicationAssembly
            .GetTypes()
            .Where(type => type.IsInterface)
            .ToArray();

        return infrastructureAssembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Select(implementation => new InfrastructureImplementation(
                Module: module,
                Implementation: implementation,
                ApplicationInterfaces:
                [
                    .. applicationInterfaces.Where(
                        applicationInterface => applicationInterface.IsAssignableFrom(implementation))
                ]))
            .Where(target => target.ApplicationInterfaces.Count > 0);
    }

    private static IEnumerable<string> GetMissingTestMethods(
        string repositoryRoot,
        InfrastructureImplementation target)
    {
        var testFilePath = GetExpectedTestFilePath(
            repositoryRoot,
            target.Module,
            target.Implementation);

        if (!File.Exists(testFilePath))
        {
            return [];
        }

        var testMethodNames = TestMethodPattern()
            .Matches(File.ReadAllText(testFilePath))
            .Select(match => match.Groups["name"].Value)
            .ToArray();
        var contractMethods = target.ApplicationInterfaces
            .SelectMany(applicationInterface =>
                applicationInterface.GetInterfaces().Append(applicationInterface))
            .SelectMany(applicationInterface => applicationInterface.GetMethods())
            .Where(method => !method.IsSpecialName)
            .Distinct()
            .GroupBy(method => method.Name, StringComparer.Ordinal);

        return contractMethods
            .Select(methods => new
            {
                MethodName = methods.Key,
                RequiredTestCount = methods.Count(),
                ActualTestCount = testMethodNames.Count(testMethodName =>
                    testMethodName.StartsWith(
                        methods.Key + "_Should_",
                        StringComparison.Ordinal))
            })
            .Where(coverage => coverage.ActualTestCount < coverage.RequiredTestCount)
            .Select(coverage =>
                $"{target.Implementation.FullName}.{coverage.MethodName}: expected " +
                $"{coverage.RequiredTestCount} test method(s) starting with " +
                $"'{coverage.MethodName}_Should_', found {coverage.ActualTestCount} " +
                $"in '{Path.GetRelativePath(repositoryRoot, testFilePath)}'.");
    }

    private static string GetExpectedTestFilePath(
        string repositoryRoot,
        ModuleArchitecture module,
        Type implementation)
    {
        var implementationNamespace = implementation.Namespace
            ?? throw new InvalidOperationException(
                $"Infrastructure implementation '{implementation.FullName}' does not have a namespace.");
        var infrastructureNamespace = module.InfrastructureAssemblyName;
        var infrastructureNamespacePrefix = infrastructureNamespace + ".";
        string relativeNamespace;

        if (string.Equals(
                implementationNamespace,
                infrastructureNamespace,
                StringComparison.Ordinal))
        {
            relativeNamespace = string.Empty;
        }
        else if (implementationNamespace.StartsWith(
                     infrastructureNamespacePrefix,
                     StringComparison.Ordinal))
        {
            relativeNamespace =
                implementationNamespace[infrastructureNamespacePrefix.Length..];
        }
        else
        {
            throw new InvalidOperationException(
                $"Infrastructure implementation namespace '{implementationNamespace}' must be " +
                $"'{infrastructureNamespace}' or start with '{infrastructureNamespacePrefix}'.");
        }

        var moduleDirectoryName = module.Name[(module.Name.LastIndexOf('.') + 1)..];
        var integrationTestsAssemblyName =
            module.Name + IntegrationTestsAssemblySuffix;
        var namespacePath = relativeNamespace.Replace(
            '.',
            Path.DirectorySeparatorChar);
        var implementationName = implementation.Name.Split('`')[0];

        return Path.Combine(
            repositoryRoot,
            TestsDirectoryName,
            moduleDirectoryName,
            integrationTestsAssemblyName,
            InfrastructureDirectoryName,
            namespacePath,
            $"{implementationName}Tests.cs");
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

    private sealed record InfrastructureImplementation(
        ModuleArchitecture Module,
        Type Implementation,
        IReadOnlyCollection<Type> ApplicationInterfaces);
}
