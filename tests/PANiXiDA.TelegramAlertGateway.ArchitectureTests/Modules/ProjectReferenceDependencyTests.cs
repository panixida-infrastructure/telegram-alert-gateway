using System.Xml.Linq;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Modules;

public sealed class ProjectReferenceDependencyTests
{
    private const string SourceDirectoryName = "src";

    [Fact(DisplayName = "Module project references should follow allowed dependencies when validated")]
    public void ModuleProjectReferences_Should_FollowAllowedDependencies_When_Validated()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPaths = GetProjectPaths(repositoryRoot);
        var modules = ArchitectureDefinition.Modules;
        var violations = modules
            .SelectMany(module => GetModuleAssemblyNames(module)
                .SelectMany(sourceAssemblyName => GetProjectReferences(
                        projectPaths[sourceAssemblyName])
                    .Where(targetAssemblyName => !IsAllowedDependency(
                        module,
                        sourceAssemblyName,
                        targetAssemblyName))
                    .Select(targetAssemblyName =>
                        $"{sourceAssemblyName} must not reference " +
                        $"{targetAssemblyName}.")))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(modules);
        Assert.True(
            violations.Length == 0,
            $"Forbidden module project references:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static Dictionary<string, string> GetProjectPaths(
        string repositoryRoot)
    {
        var sourceRoot = Path.Combine(
            repositoryRoot,
            SourceDirectoryName);

        return Directory
            .EnumerateFiles(
                sourceRoot,
                "*.csproj",
                SearchOption.AllDirectories)
            .Select(projectPath => new
            {
                Name = Path.GetFileNameWithoutExtension(projectPath)
                    ?? throw new InvalidOperationException(
                        $"Could not determine project name for " +
                        $"'{projectPath}'."),
                Path = projectPath
            })
            .ToDictionary(
                project => project.Name,
                project => project.Path,
                StringComparer.Ordinal);
    }

    private static IEnumerable<string> GetProjectReferences(
        string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException(
                $"Could not determine directory for '{projectPath}'.");
        var project = XDocument.Load(projectPath);

        return project
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Cast<string>()
            .Select(include => Path.GetFullPath(
                Path.Combine(
                    projectDirectory,
                    include
                        .Replace(
                            '\\',
                            Path.DirectorySeparatorChar)
                        .Replace(
                            '/',
                            Path.DirectorySeparatorChar))))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(projectName => !string.IsNullOrWhiteSpace(projectName))
            .Cast<string>();
    }

    private static bool IsAllowedDependency(
        ModuleArchitecture module,
        string sourceAssemblyName,
        string targetAssemblyName)
    {
        if (targetAssemblyName.EndsWith(
                ".Contracts",
                StringComparison.Ordinal))
        {
            return sourceAssemblyName != module.ContractsAssemblyName &&
                   sourceAssemblyName != module.DomainAssemblyName;
        }

        if (sourceAssemblyName == module.ApplicationAssemblyName)
        {
            return targetAssemblyName == module.DomainAssemblyName;
        }

        if (sourceAssemblyName == module.InfrastructureAssemblyName)
        {
            return targetAssemblyName == module.DomainAssemblyName ||
                   targetAssemblyName == module.ApplicationAssemblyName;
        }

        if (sourceAssemblyName == module.PresentationAssemblyName)
        {
            return targetAssemblyName == module.ApplicationAssemblyName;
        }

        return false;
    }

    private static IReadOnlyCollection<string> GetModuleAssemblyNames(
        ModuleArchitecture module)
    {
        return
        [
            module.ContractsAssemblyName,
            module.DomainAssemblyName,
            module.ApplicationAssemblyName,
            module.InfrastructureAssemblyName,
            module.PresentationAssemblyName
        ];
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
