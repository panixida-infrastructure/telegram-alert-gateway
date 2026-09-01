using PANiXiDA.Core.Presentation.Http.Endpoints;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Presentation;

public sealed class EndpointFunctionalTestConventionTests
{
    private const string PresentationAssemblySuffix = ".Presentation";
    private const string FunctionalTestsAssemblySuffix = ".FunctionalTests";
    private const string SourceDirectoryName = "src";
    private const string TestsDirectoryName = "tests";

    [Fact(DisplayName = "Endpoints should have matching functional test files when declared")]
    public void Endpoints_Should_HaveMatchingFunctionalTestFiles_When_Declared()
    {
        var repositoryRoot = FindRepositoryRoot();
        var endpoints = ArchitectureDefinition.ProductionAssemblies
            .Where(assembly => assembly.GetName().Name?.EndsWith(
                PresentationAssemblySuffix,
                StringComparison.Ordinal) == true)
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type =>
                type is { IsClass: true, IsAbstract: false } &&
                typeof(IEndpoint).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(endpoints);

        var missingTestFiles = endpoints
            .Select(endpoint => GetExpectedTestFilePath(repositoryRoot, endpoint))
            .Where(expectedPath => !File.Exists(expectedPath))
            .Select(expectedPath => Path.GetRelativePath(repositoryRoot, expectedPath))
            .ToArray();

        Assert.True(
            missingTestFiles.Length == 0,
            $"Missing functional test files:{Environment.NewLine}" +
            string.Join(Environment.NewLine, missingTestFiles));
    }

    private static string GetExpectedTestFilePath(
        string repositoryRoot,
        Type endpoint)
    {
        var presentationAssemblyName = endpoint.Assembly.GetName().Name
            ?? throw new InvalidOperationException(
                $"Could not determine the assembly for '{endpoint.FullName}'.");
        var endpointNamespace = endpoint.Namespace
            ?? throw new InvalidOperationException(
                $"Endpoint '{endpoint.FullName}' does not have a namespace.");
        var moduleName = presentationAssemblyName[..^PresentationAssemblySuffix.Length];
        var moduleDirectoryName = moduleName[(moduleName.LastIndexOf('.') + 1)..];
        var functionalTestsAssemblyName =
            moduleName + FunctionalTestsAssemblySuffix;
        var presentationNamespacePrefix =
            presentationAssemblyName + ".";

        if (!endpointNamespace.StartsWith(
                presentationNamespacePrefix,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Endpoint namespace '{endpointNamespace}' must start with " +
                $"'{presentationNamespacePrefix}'.");
        }

        var relativeNamespace = endpointNamespace[presentationNamespacePrefix.Length..];
        var namespacePath = relativeNamespace.Replace(
            '.',
            Path.DirectorySeparatorChar);

        return Path.Combine(
            repositoryRoot,
            TestsDirectoryName,
            moduleDirectoryName,
            functionalTestsAssemblyName,
            "Presentation",
            namespacePath,
            $"{endpoint.Name}Tests.cs");
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
}
