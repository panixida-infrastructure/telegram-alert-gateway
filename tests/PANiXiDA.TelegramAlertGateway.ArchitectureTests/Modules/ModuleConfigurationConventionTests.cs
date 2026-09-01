using System.Reflection;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Modules;

public sealed class ModuleConfigurationConventionTests
{
    private const string HostAssemblyName = "PANiXiDA.TelegramAlertGateway.Host";
    private const string ModuleConfigurationsNamespace =
        $"{HostAssemblyName}.Configurations.Modules";
    private const string SourceDirectoryName = "src";

    [Fact(DisplayName = "Modules should have host configurations in modules directory when discovered")]
    public void Modules_Should_HaveHostConfigurationsInModulesDirectory_When_Discovered()
    {
        var modules = ArchitectureDefinition.Modules;
        var hostAssembly = ArchitectureDefinition.ProductionAssemblies.Single(
            assembly => string.Equals(
                assembly.GetName().Name,
                HostAssemblyName,
                StringComparison.Ordinal));
        var repositoryRoot = FindRepositoryRoot();

        var violations = modules
            .Select(module => GetConfigurationViolation(
                module: module,
                hostAssembly: hostAssembly,
                repositoryRoot: repositoryRoot))
            .OfType<string>()
            .ToArray();

        Assert.NotEmpty(modules);
        Assert.True(
            violations.Length == 0,
            $"Every module must have a host configuration in " +
            $"'{ModuleConfigurationsNamespace}':{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static string? GetConfigurationViolation(
        ModuleArchitecture module,
        Assembly hostAssembly,
        string repositoryRoot)
    {
        var moduleName = module.Name[(module.Name.LastIndexOf('.') + 1)..];
        var configurationName = $"{moduleName}ModuleConfiguration";
        var configurationTypeName =
            $"{ModuleConfigurationsNamespace}.{configurationName}";
        var relativeConfigurationPath = Path.Combine(
            SourceDirectoryName,
            HostAssemblyName,
            "Configurations",
            "Modules",
            $"{configurationName}.cs");
        var configurationPath = Path.Combine(
            repositoryRoot,
            relativeConfigurationPath);

        if (hostAssembly.GetType(configurationTypeName) is null)
        {
            return $"Module '{module.Name}' must declare " +
                   $"'{configurationTypeName}'.";
        }

        if (!File.Exists(configurationPath))
        {
            return $"Module '{module.Name}' configuration must reside at " +
                   $"'{relativeConfigurationPath}'.";
        }

        return null;
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
