using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Global;

public sealed class NamespaceConventionTests
{
    [Fact(DisplayName = "Namespaces should match folder structure when declared")]
    public void Namespaces_Should_MatchFolderStructure_When_Declared()
    {
        var sourceNamespaces = NamespaceSourceDiscovery.GetSourceNamespaces();
        var violations = sourceNamespaces
            .Where(sourceNamespace => !string.Equals(
                sourceNamespace.ActualNamespace,
                sourceNamespace.ExpectedNamespace,
                StringComparison.Ordinal))
            .Select(sourceNamespace =>
                $"{sourceNamespace.RelativePath}: namespace " +
                $"'{sourceNamespace.ActualNamespace}' must be " +
                $"'{sourceNamespace.ExpectedNamespace}'.")
            .ToArray();

        Assert.NotEmpty(sourceNamespaces);
        Assert.True(
            violations.Length == 0,
            $"Namespaces must match their project-relative folder " +
            $"structure:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }
}

internal static class NamespaceSourceDiscovery
{
    private static readonly string[] SourceRootDirectoryNames =
    [
        "src",
        "tests",
        "tools"
    ];

    private static readonly string[] ExcludedDirectoryNames =
    [
        "bin",
        "obj",
        "Generated"
    ];

    internal static SourceNamespace[] GetSourceNamespaces()
    {
        var repositoryRoot = FindRepositoryRoot();

        return
        [
            .. GetProjectFiles(repositoryRoot)
                .SelectMany(projectFile =>
                    GetProjectSourceNamespaces(
                        repositoryRoot,
                        projectFile))
                .OrderBy(
                    sourceNamespace => sourceNamespace.RelativePath,
                    StringComparer.Ordinal)
                .ThenBy(
                    sourceNamespace => sourceNamespace.ActualNamespace,
                    StringComparer.Ordinal)
        ];
    }

    private static IEnumerable<string> GetProjectFiles(
        string repositoryRoot)
    {
        return SourceRootDirectoryNames
            .Select(directoryName =>
                Path.Combine(repositoryRoot, directoryName))
            .Where(Directory.Exists)
            .SelectMany(sourceRoot =>
                Directory.EnumerateFiles(
                    sourceRoot,
                    "*.csproj",
                    SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal);
    }

    private static IEnumerable<SourceNamespace> GetProjectSourceNamespaces(
        string repositoryRoot,
        string projectFile)
    {
        var projectDirectory = Path.GetDirectoryName(projectFile)
            ?? throw new InvalidOperationException(
                $"Project '{projectFile}' does not have a directory.");
        var rootNamespace = GetRootNamespace(projectFile);

        return Directory
            .EnumerateFiles(
                projectDirectory,
                "*.cs",
                SearchOption.AllDirectories)
            .Where(IsSourceFile)
            .Order(StringComparer.Ordinal)
            .SelectMany(sourceFile =>
                GetSourceNamespaces(
                    repositoryRoot,
                    projectDirectory,
                    rootNamespace,
                    sourceFile));
    }

    private static IEnumerable<SourceNamespace> GetSourceNamespaces(
        string repositoryRoot,
        string projectDirectory,
        string rootNamespace,
        string sourceFile)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            File.ReadAllText(sourceFile));
        var root = syntaxTree.GetCompilationUnitRoot();
        var expectedNamespace = GetExpectedNamespace(
            projectDirectory,
            rootNamespace,
            sourceFile);
        var relativePath = Path.GetRelativePath(
            repositoryRoot,
            sourceFile);
        var namespaceDeclarations = root
            .DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Where(ContainsDirectTypeDeclaration)
            .Select(GetFullNamespace)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var actualNamespace in namespaceDeclarations)
        {
            yield return new SourceNamespace(
                relativePath,
                expectedNamespace,
                actualNamespace);
        }

        if (ContainsGlobalTypeDeclaration(root))
        {
            yield return new SourceNamespace(
                relativePath,
                expectedNamespace,
                "<global namespace>");
        }
    }

    private static string GetRootNamespace(string projectFile)
    {
        var configuredRootNamespaces = XDocument
            .Load(projectFile)
            .Descendants()
            .Where(element =>
                string.Equals(
                    element.Name.LocalName,
                    "RootNamespace",
                    StringComparison.Ordinal))
            .Select(element => element.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return configuredRootNamespaces.Length switch
        {
            0 => Path.GetFileNameWithoutExtension(projectFile),
            1 => configuredRootNamespaces[0],
            _ => throw new InvalidOperationException(
                $"Project '{projectFile}' declares multiple root namespaces: " +
                $"{string.Join(", ", configuredRootNamespaces)}.")
        };
    }

    private static string GetExpectedNamespace(
        string projectDirectory,
        string rootNamespace,
        string sourceFile)
    {
        var sourceDirectory = Path.GetDirectoryName(sourceFile)
            ?? throw new InvalidOperationException(
                $"Source file '{sourceFile}' does not have a directory.");
        var relativeDirectory = Path.GetRelativePath(
            projectDirectory,
            sourceDirectory);

        if (string.Equals(
            relativeDirectory,
            ".",
            StringComparison.Ordinal))
        {
            return rootNamespace;
        }

        var namespaceSuffix = string.Join(
            '.',
            relativeDirectory.Split(
                [
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                ],
                StringSplitOptions.RemoveEmptyEntries));

        return $"{rootNamespace}.{namespaceSuffix}";
    }

    private static bool ContainsDirectTypeDeclaration(
        BaseNamespaceDeclarationSyntax namespaceDeclaration)
    {
        return namespaceDeclaration.Members.Any(IsTypeDeclaration);
    }

    private static bool ContainsGlobalTypeDeclaration(
        CompilationUnitSyntax root)
    {
        return root.Members.Any(IsTypeDeclaration);
    }

    private static bool IsTypeDeclaration(MemberDeclarationSyntax member)
    {
        return member is BaseTypeDeclarationSyntax or
            DelegateDeclarationSyntax;
    }

    private static string GetFullNamespace(
        BaseNamespaceDeclarationSyntax namespaceDeclaration)
    {
        return string.Join(
            '.',
            namespaceDeclaration
                .AncestorsAndSelf()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(declaration => declaration.Name.ToString()));
    }

    private static bool IsSourceFile(string path)
    {
        return !path
            .Split(
                [
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                ],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment =>
                ExcludedDirectoryNames.Contains(
                    segment,
                    StringComparer.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (SourceRootDirectoryNames.All(directoryName =>
                Directory.Exists(Path.Combine(
                    directory.FullName,
                    directoryName))))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find repository root containing " +
            $"{string.Join(", ", SourceRootDirectoryNames)} directories.");
    }
}

internal sealed record SourceNamespace(
    string RelativePath,
    string ExpectedNamespace,
    string ActualNamespace);
