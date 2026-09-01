using System.Text.Json;
using System.Text.RegularExpressions;

using ArchUnitNET.Domain;
using ArchUnitNET.Loader;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

using ReflectionAssembly = System.Reflection.Assembly;
using ReflectionAssemblyName = System.Reflection.AssemblyName;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Definitions;

internal static class ArchitectureDefinition
{
    private const string ContractsLayerSuffix = ".Contracts";
    private const string DomainLayerSuffix = ".Domain";
    private const string ApplicationLayerSuffix = ".Application";
    private const string InfrastructureLayerSuffix = ".Infrastructure";
    private const string PresentationLayerSuffix = ".Presentation";
    private const string HostLayerSuffix = ".Host";

    internal static readonly IReadOnlyCollection<ReflectionAssembly> ProductionAssemblies =
        LoadProductionAssemblies();

    private static readonly IReadOnlyCollection<string> ProductionAssemblyNames =
        [.. ProductionAssemblies
            .Select(assembly => assembly.GetName().Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()];

    private static readonly ModuleDiscoveryResult ModuleDiscoveryResult =
        DiscoverModules(ProductionAssemblyNames);

    internal static string IncludeNamespaceSegment(string namespaceSegment)
    {
        return $".*\\.{Regex.Escape(namespaceSegment)}(\\..*)?$";
    }

    internal static readonly Architecture Architecture =
        new ArchLoader().LoadAssemblies([.. ProductionAssemblies]).Build();

    internal static readonly IReadOnlyCollection<ModuleArchitecture> Modules =
        ModuleDiscoveryResult.Modules;

    internal static readonly IReadOnlyCollection<string> ModuleDiscoveryErrors =
        ModuleDiscoveryResult.Errors;

    internal static string DomainAssemblyNamePattern =>
        AssemblyNamePattern(DomainLayerSuffix);

    internal static string ContractsAssemblyNamePattern =>
        AssemblyNamePattern(ContractsLayerSuffix);

    internal static string ApplicationAssemblyNamePattern =>
        AssemblyNamePattern(ApplicationLayerSuffix);

    internal static string InfrastructureAssemblyNamePattern =>
        AssemblyNamePattern(InfrastructureLayerSuffix);

    internal static string PresentationAssemblyNamePattern =>
        AssemblyNamePattern(PresentationLayerSuffix);

    internal static readonly IObjectProvider<IType> DomainLayer =
        Types().That().ResideInAssemblyMatching(DomainAssemblyNamePattern).As("Domain layer");

    internal static readonly IObjectProvider<IType> ContractsLayer =
        Types().That().ResideInAssemblyMatching(ContractsAssemblyNamePattern).As("Contracts layer");

    internal static readonly IObjectProvider<IType> ApplicationLayer =
        Types().That().ResideInAssemblyMatching(ApplicationAssemblyNamePattern).As("Application layer");

    internal static readonly IObjectProvider<IType> InfrastructureLayer =
        Types().That().ResideInAssemblyMatching(InfrastructureAssemblyNamePattern).As("Infrastructure layer");

    internal static readonly IObjectProvider<IType> PresentationLayer =
        Types().That().ResideInAssemblyMatching(PresentationAssemblyNamePattern).As("Presentation layer");

    internal static readonly IObjectProvider<IType> HostLayer =
        Types().That().ResideInAssemblyMatching(AssemblyNamePattern(HostLayerSuffix)).As("Host layer");

    internal static IObjectProvider<IType> TypesInAssembly(string assemblyName)
    {
        return Types()
            .That()
            .ResideInAssemblyMatching(
                $"^{Regex.Escape(assemblyName)}(,.*)?$");
    }

    private static string AssemblyNamePattern(string suffix)
    {
        return $".*{Regex.Escape(suffix)}(,.*)?$";
    }

    private static ReflectionAssembly[] LoadProductionAssemblies()
    {
        return [.. GetProjectAssemblyNames()
            .Where(IsArchitecturalAssemblyName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(LoadAssembly)];
    }

    private static string[] GetProjectAssemblyNames()
    {
        var executingAssemblyName = ReflectionAssembly.GetExecutingAssembly().GetName().Name;
        var depsFilePath = Path.Combine(
            AppContext.BaseDirectory,
            $"{executingAssemblyName}.deps.json");

        using var depsFile = File.OpenRead(depsFilePath);
        using var depsDocument = JsonDocument.Parse(depsFile);

        return [.. depsDocument.RootElement
            .GetProperty("libraries")
            .EnumerateObject()
            .Where(library =>
                library.Value.TryGetProperty("type", out var type) &&
                type.GetString() == "project")
            .Select(library => library.Name.Split('/')[0])
            .Where(assemblyName => assemblyName != executingAssemblyName)];
    }

    private static bool IsArchitecturalAssemblyName(string assemblyName)
    {
        return IsLayerAssemblyName(assemblyName) ||
               assemblyName.EndsWith(HostLayerSuffix, StringComparison.Ordinal);
    }

    private static bool IsLayerAssemblyName(string assemblyName)
    {
        return assemblyName.EndsWith(ContractsLayerSuffix, StringComparison.Ordinal) ||
               assemblyName.EndsWith(DomainLayerSuffix, StringComparison.Ordinal) ||
               assemblyName.EndsWith(ApplicationLayerSuffix, StringComparison.Ordinal) ||
               assemblyName.EndsWith(InfrastructureLayerSuffix, StringComparison.Ordinal) ||
               assemblyName.EndsWith(PresentationLayerSuffix, StringComparison.Ordinal);
    }

    private static ReflectionAssembly LoadAssembly(string assemblyName)
    {
        var assemblyPath = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");

        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException(
                $"Project assembly '{assemblyName}' was not found in architecture test output.",
                assemblyPath);
        }

        var assemblyNameFromPath = ReflectionAssemblyName.GetAssemblyName(assemblyPath);

        return ReflectionAssembly.Load(assemblyNameFromPath);
    }

    private static ModuleDiscoveryResult DiscoverModules(
        IReadOnlyCollection<string> assemblyNames)
    {
        var modules = assemblyNames
            .Where(IsLayerAssemblyName)
            .GroupBy(GetModuleName)
            .Select(CreateModuleArchitecture)
            .ToArray();

        return new ModuleDiscoveryResult(
            [.. modules.Where(module => module.Error is null).Select(module => module.Module!)],
            [.. modules.Where(module => module.Error is not null).Select(module => module.Error!)]);
    }

    private static string GetModuleName(string assemblyName)
    {
        var layerSuffix = GetLayerSuffix(assemblyName);

        return assemblyName[..^layerSuffix.Length];
    }

    private static string GetLayerSuffix(string assemblyName)
    {
        return new[]
            {
                ContractsLayerSuffix,
                DomainLayerSuffix,
                ApplicationLayerSuffix,
                InfrastructureLayerSuffix,
                PresentationLayerSuffix
            }
            .Single(suffix => assemblyName.EndsWith(suffix, StringComparison.Ordinal));
    }

    private static (ModuleArchitecture? Module, string? Error) CreateModuleArchitecture(
        IGrouping<string, string> moduleAssemblies)
    {
        var assemblies = moduleAssemblies.ToDictionary(GetLayerSuffix, StringComparer.Ordinal);
        var missingLayers = new[]
            {
                ContractsLayerSuffix,
                DomainLayerSuffix,
                ApplicationLayerSuffix,
                InfrastructureLayerSuffix,
                PresentationLayerSuffix
            }
            .Where(layerSuffix => !assemblies.ContainsKey(layerSuffix))
            .ToArray();

        if (missingLayers.Length > 0)
        {
            return (
                null,
                $"Module '{moduleAssemblies.Key}' is missing layer assemblies: {string.Join(", ", missingLayers)}.");
        }

        return (
            new ModuleArchitecture(
                moduleAssemblies.Key,
                assemblies[ContractsLayerSuffix],
                assemblies[DomainLayerSuffix],
                assemblies[ApplicationLayerSuffix],
                assemblies[InfrastructureLayerSuffix],
                assemblies[PresentationLayerSuffix]),
            null);
    }
}
