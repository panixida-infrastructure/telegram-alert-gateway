using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using PANiXiDA.Core.Domain.AggregateRoots;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Definitions;

internal static class InfrastructurePersistenceConvention
{
    private const string SourceDirectoryName = "src";

    internal static Type[] GetConcreteInfrastructureTypes(
        Func<Type, bool> predicate)
    {
        return
        [
            .. ArchitectureDefinition.Modules
                .Select(module =>
                    ArchitectureDefinition.ProductionAssemblies.Single(
                        assembly => string.Equals(
                            assembly.GetName().Name,
                            module.InfrastructureAssemblyName,
                            StringComparison.Ordinal)))
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type =>
                    type is { IsClass: true, IsAbstract: false } &&
                    predicate(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
        ];
    }

    internal static Type? GetClosedGenericBaseType(
        Type type,
        Type openGenericType)
    {
        for (var currentType = type;
             currentType is not null;
             currentType = currentType.BaseType)
        {
            if (currentType.IsGenericType &&
                currentType.GetGenericTypeDefinition() == openGenericType)
            {
                return currentType;
            }
        }

        return null;
    }

    internal static Type? GetClosedGenericInterface(
        Type type,
        Type openGenericType)
    {
        return type
            .GetInterfaces()
            .Append(type)
            .FirstOrDefault(candidate =>
                candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == openGenericType);
    }

    internal static ModuleArchitecture GetModule(Type infrastructureType)
    {
        var assemblyName = infrastructureType.Assembly.GetName().Name
            ?? throw new InvalidOperationException(
                $"Could not determine assembly for " +
                $"'{infrastructureType.FullName}'.");

        return ArchitectureDefinition.Modules.Single(module =>
            string.Equals(
                module.InfrastructureAssemblyName,
                assemblyName,
                StringComparison.Ordinal));
    }

    internal static string GetModuleShortName(ModuleArchitecture module)
    {
        return module.Name[(module.Name.LastIndexOf('.') + 1)..];
    }

    internal static string[] GetAggregateFeatureNames(
        ModuleArchitecture module)
    {
        return
        [
            .. GetAggregateRootTypes(module)
                .Select(type =>
                    EnglishNamingConvention.Pluralize(type.Name))
                .Order(StringComparer.Ordinal)
        ];
    }

    internal static Type[] GetAggregateRootTypes(
        ModuleArchitecture module)
    {
        var domainAssembly = ArchitectureDefinition.ProductionAssemblies
            .Single(assembly => string.Equals(
                assembly.GetName().Name,
                module.DomainAssemblyName,
                StringComparison.Ordinal));

        return
        [
            .. domainAssembly
                .GetTypes()
                .Where(type =>
                    type is { IsClass: true, IsAbstract: false } &&
                    typeof(IAggregateRoot).IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
        ];
    }

    internal static IEnumerable<string> GetLocationViolations(
        Type type,
        params string[] relativeNamespaceSegments)
    {
        return GetLocationViolations(
            type,
            [relativeNamespaceSegments]);
    }

    internal static IEnumerable<string> GetAggregateFeatureLocationViolations(
        Type type,
        params string[] trailingNamespaceSegments)
    {
        var module = GetModule(type);
        var relativeNamespaces = GetAggregateFeatureNames(module)
            .Select(featureName => new[]
                {
                    "Persistence",
                    "Features",
                    featureName
                }
                .Concat(trailingNamespaceSegments)
                .ToArray())
            .ToArray();

        return GetLocationViolations(type, relativeNamespaces);
    }

    internal static string[] FindSourceFiles(Type type)
    {
        var projectDirectory = GetProjectDirectory(type);
        var expectedNamespace = type.Namespace
            ?? throw new InvalidOperationException(
                $"Type '{type.FullName}' does not have a namespace.");

        return
        [
            .. Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*.cs",
                    SearchOption.AllDirectories)
                .Where(path =>
                    !HasPathSegment(path, "bin") &&
                    !HasPathSegment(path, "obj"))
                .Where(path => DeclaresType(
                    path,
                    expectedNamespace,
                    type.Name.Split('`')[0]))
                .Order(StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static List<string> GetLocationViolations(
        Type type,
        IReadOnlyCollection<string[]> relativeNamespaceCandidates)
    {
        var module = GetModule(type);
        var expectedNamespaces = relativeNamespaceCandidates
            .Select(segments =>
                module.InfrastructureAssemblyName + "." +
                string.Join(".", segments))
            .ToArray();
        var expectedDirectories = relativeNamespaceCandidates
            .Select(segments => Path.Combine(
                GetProjectDirectory(type),
                Path.Combine(segments)))
            .ToArray();
        var sourceFiles = FindSourceFiles(type);
        var violations = new List<string>();

        if (!expectedNamespaces.Contains(
                type.Namespace,
                StringComparer.Ordinal))
        {
            violations.Add(
                $"{type.FullName} must reside in one of these namespaces: " +
                $"{FormatValues(expectedNamespaces)}.");
        }

        if (sourceFiles.Length == 0)
        {
            violations.Add(
                $"{type.FullName} must have a matching source declaration.");
        }

        violations.AddRange(sourceFiles
            .Where(sourceFile =>
                !expectedDirectories.Any(expectedDirectory =>
                    string.Equals(
                        Path.GetDirectoryName(sourceFile),
                        expectedDirectory,
                        StringComparison.OrdinalIgnoreCase)))
            .Select(sourceFile =>
                $"{type.FullName} source file " +
                $"'{Path.GetRelativePath(
                    FindRepositoryRoot(),
                    sourceFile)}' must reside in one of these directories: " +
                $"{FormatValues(expectedDirectories.Select(directory =>
                    Path.GetRelativePath(
                        FindRepositoryRoot(),
                        directory)))}."));

        return violations;
    }

    private static string GetProjectDirectory(Type type)
    {
        var module = GetModule(type);

        return Path.Combine(
            FindRepositoryRoot(),
            SourceDirectoryName,
            GetModuleShortName(module),
            module.InfrastructureAssemblyName);
    }

    private static bool DeclaresType(
        string sourceFile,
        string expectedNamespace,
        string expectedTypeName)
    {
        var syntaxRoot = CSharpSyntaxTree
            .ParseText(File.ReadAllText(sourceFile))
            .GetRoot();

        return syntaxRoot
            .DescendantNodes()
            .OfType<BaseTypeDeclarationSyntax>()
            .Any(declaration =>
                string.Equals(
                    declaration.Identifier.ValueText,
                    expectedTypeName,
                    StringComparison.Ordinal) &&
                string.Equals(
                    GetNamespace(declaration),
                    expectedNamespace,
                    StringComparison.Ordinal));
    }

    private static string GetNamespace(
        BaseTypeDeclarationSyntax declaration)
    {
        return string.Join(
            ".",
            declaration
                .Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(namespaceDeclaration =>
                    namespaceDeclaration.Name.ToString()));
    }

    private static bool HasPathSegment(
        string path,
        string segment)
    {
        return path
            .Split(
                [
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                ],
                StringSplitOptions.RemoveEmptyEntries)
            .Contains(segment, StringComparer.OrdinalIgnoreCase);
    }

    private static string FormatValues(IEnumerable<string> values)
    {
        return string.Join(
            ", ",
            values.Select(value => $"'{value}'"));
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
