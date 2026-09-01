using System.Collections.Concurrent;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using PANiXiDA.Core.Presentation.Http.Endpoints;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Definitions;

internal static class PresentationArchitectureConvention
{
    private const string MapperAttributeFullName =
        "Riok.Mapperly.Abstractions.MapperAttribute";
    private const string SourceDirectoryName = "src";

    private static readonly ConcurrentDictionary<string, string[]>
        ProjectSourceFiles = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, SyntaxNode>
        SourceRoots = new(StringComparer.OrdinalIgnoreCase);

    internal static Type[] GetConcretePresentationTypes(
        Func<Type, bool> predicate)
    {
        return GetPresentationTypes(type =>
            !type.IsAbstract &&
            predicate(type));
    }

    internal static Type[] GetPresentationTypes(
        Func<Type, bool> predicate)
    {
        return
        [
            .. ArchitectureDefinition.Modules
                .Select(module =>
                    ArchitectureDefinition.ProductionAssemblies.Single(
                        assembly => string.Equals(
                            assembly.GetName().Name,
                            module.PresentationAssemblyName,
                            StringComparison.Ordinal)))
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type =>
                    type is { IsClass: true } &&
                    type.Namespace is not null &&
                    type.Namespace.StartsWith(
                        type.Assembly.GetName().Name + ".",
                        StringComparison.Ordinal) &&
                    predicate(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
        ];
    }

    internal static Type[] GetEndpointGroups()
    {
        return GetConcretePresentationTypes(type =>
            typeof(IEndpointGroup).IsAssignableFrom(type));
    }

    internal static Type[] GetEndpoints()
    {
        return GetConcretePresentationTypes(type =>
            typeof(IEndpoint).IsAssignableFrom(type));
    }

    internal static Type GetEndpointGroup(Type endpoint)
    {
        var endpointContract = GetClosedGenericInterface(
            endpoint,
            typeof(IEndpoint<>))
            ?? throw new InvalidOperationException(
                $"{endpoint.FullName} must implement IEndpoint<TGroup>.");

        return endpointContract.GetGenericArguments()[0];
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

    internal static bool IsMapperlyMapper(Type type)
    {
        return type.CustomAttributes.Any(attribute =>
                   string.Equals(
                       attribute.AttributeType.FullName,
                       MapperAttributeFullName,
                       StringComparison.Ordinal)) ||
               GetSourceSyntax(type)
                   .SelectMany(source => source.Root
                       .DescendantNodes()
                       .OfType<BaseTypeDeclarationSyntax>())
                   .Where(declaration => string.Equals(
                       declaration.Identifier.ValueText,
                       type.Name.Split('`')[0],
                       StringComparison.Ordinal))
                   .SelectMany(declaration => declaration.AttributeLists)
                   .SelectMany(attributeList => attributeList.Attributes)
                   .Any(attribute =>
                       attribute.Name.ToString() is
                           "Mapper" or
                           "MapperAttribute" or
                           "Riok.Mapperly.Abstractions.Mapper" or
                           "Riok.Mapperly.Abstractions.MapperAttribute");
    }

    internal static ModuleArchitecture GetModule(Type presentationType)
    {
        var assemblyName = presentationType.Assembly.GetName().Name
            ?? throw new InvalidOperationException(
                $"Could not determine assembly for " +
                $"'{presentationType.FullName}'.");

        return ArchitectureDefinition.Modules.Single(module =>
            string.Equals(
                module.PresentationAssemblyName,
                assemblyName,
                StringComparison.Ordinal));
    }

    internal static string GetRelativeNamespace(Type type)
    {
        var assemblyName = type.Assembly.GetName().Name
            ?? throw new InvalidOperationException(
                $"Could not determine assembly for '{type.FullName}'.");
        var typeNamespace = type.Namespace
            ?? throw new InvalidOperationException(
                $"Type '{type.FullName}' does not have a namespace.");
        var namespacePrefix = assemblyName + ".";

        if (!typeNamespace.StartsWith(
                namespacePrefix,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Type '{type.FullName}' namespace must start with " +
                $"'{namespacePrefix}'.");
        }

        return typeNamespace[namespacePrefix.Length..];
    }

    internal static IEnumerable<string> GetLocationViolations(
        Type type,
        params string[] relativeNamespaceSegments)
    {
        var module = GetModule(type);
        var expectedNamespace =
            module.PresentationAssemblyName + "." +
            string.Join(".", relativeNamespaceSegments);
        var expectedDirectory = Path.Combine(
            GetProjectDirectory(type),
            Path.Combine(relativeNamespaceSegments));
        var sourceFiles = FindSourceFiles(type);
        var violations = new List<string>();

        if (!string.Equals(
                type.Namespace,
                expectedNamespace,
                StringComparison.Ordinal))
        {
            violations.Add(
                $"{type.FullName} must reside in namespace " +
                $"'{expectedNamespace}'.");
        }

        if (sourceFiles.Length == 0)
        {
            violations.Add(
                $"{type.FullName} must have a matching source declaration.");
        }

        violations.AddRange(sourceFiles
            .Where(sourceFile => !string.Equals(
                Path.GetDirectoryName(sourceFile),
                expectedDirectory,
                StringComparison.OrdinalIgnoreCase))
            .Select(sourceFile =>
                $"{type.FullName} source file " +
                $"'{Path.GetRelativePath(
                    FindRepositoryRoot(),
                    sourceFile)}' must reside in " +
                $"'{Path.GetRelativePath(
                    FindRepositoryRoot(),
                    expectedDirectory)}'."));

        return violations;
    }

    internal static string[] FindSourceFiles(Type type)
    {
        var projectDirectory = GetProjectDirectory(type);
        var expectedNamespace = type.Namespace
            ?? throw new InvalidOperationException(
                $"Type '{type.FullName}' does not have a namespace.");

        return
        [
            .. ProjectSourceFiles
                .GetOrAdd(
                    projectDirectory,
                    static directory =>
                    [
                        .. Directory
                            .EnumerateFiles(
                                directory,
                                "*.cs",
                                SearchOption.AllDirectories)
                            .Where(path =>
                                !HasPathSegment(path, "bin") &&
                                !HasPathSegment(path, "obj"))
                    ])
                .Where(path => DeclaresType(
                    path,
                    expectedNamespace,
                    type.Name.Split('`')[0]))
                .Order(StringComparer.OrdinalIgnoreCase)
        ];
    }

    internal static SourceSyntax[] GetSourceSyntax(Type type)
    {
        return
        [
            .. FindSourceFiles(type)
                .Select(sourceFile => new SourceSyntax(
                    SourceFile: sourceFile,
                    Root: GetSourceRoot(sourceFile)))
        ];
    }

    internal static Type? ResolvePresentationType(
        Type owner,
        string typeName)
    {
        var simpleTypeName = typeName
            .Split('.')
            .Last()
            .Split('<')[0];

        return owner.Assembly
            .GetTypes()
            .SingleOrDefault(type => string.Equals(
                type.Name.Split('`')[0],
                simpleTypeName,
                StringComparison.Ordinal));
    }

    private static string GetProjectDirectory(Type type)
    {
        var module = GetModule(type);
        var moduleDirectoryName =
            module.Name[(module.Name.LastIndexOf('.') + 1)..];

        return Path.Combine(
            FindRepositoryRoot(),
            SourceDirectoryName,
            moduleDirectoryName,
            module.PresentationAssemblyName);
    }

    private static bool DeclaresType(
        string sourceFile,
        string expectedNamespace,
        string expectedTypeName)
    {
        var syntaxRoot = GetSourceRoot(sourceFile);

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

    private static SyntaxNode GetSourceRoot(string sourceFile)
    {
        return SourceRoots.GetOrAdd(
            sourceFile,
            static path => CSharpSyntaxTree
                .ParseText(File.ReadAllText(path))
                .GetRoot());
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

internal sealed record SourceSyntax(
    string SourceFile,
    SyntaxNode Root);
