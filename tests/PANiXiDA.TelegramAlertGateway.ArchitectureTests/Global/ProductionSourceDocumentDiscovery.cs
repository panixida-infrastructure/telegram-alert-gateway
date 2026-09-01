using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Global;

internal static class ProductionSourceDocumentDiscovery
{
    private const string SourceRootDirectoryName = "src";

    private static readonly string[] AuthorSourceRootDirectoryNames =
    [
        SourceRootDirectoryName,
        "tests",
        "tools"
    ];

    private static readonly Lazy<Task<ProductionSourceContext>> Context =
        new(CreateContextAsync);

    private static readonly string[] ProductionSourceRootDirectoryNames =
    [
        SourceRootDirectoryName
    ];

    private static readonly string[] ExcludedDirectoryNames =
    [
        "bin",
        "obj",
        "Generated",
        "Migrations"
    ];

    internal static Task<T[]> GetAuthorItemsAsync<T>(
        Func<string, Document, Task<T[]>> getDocumentItemsAsync)
    {
        return GetItemsAsync(
            getDocumentItemsAsync,
            AuthorSourceRootDirectoryNames);
    }

    internal static Task<T[]> GetItemsAsync<T>(
        Func<string, Document, Task<T[]>> getDocumentItemsAsync)
    {
        return GetItemsAsync(
            getDocumentItemsAsync,
            ProductionSourceRootDirectoryNames);
    }

    private static async Task<T[]> GetItemsAsync<T>(
        Func<string, Document, Task<T[]>> getDocumentItemsAsync,
        IReadOnlyCollection<string> sourceRootDirectoryNames)
    {
        var context = await Context.Value;
        var items = new List<T>();

        foreach (var project in context.Solution.Projects
                     .Where(project =>
                         IsSourceProject(
                             context.RepositoryRoot,
                             project.FilePath,
                             sourceRootDirectoryNames))
                     .OrderBy(
                         project => project.FilePath,
                         StringComparer.Ordinal))
        {
            foreach (var document in project.Documents
                         .Where(document =>
                             IsSourceFile(
                                 context.RepositoryRoot,
                                 document.FilePath,
                                 sourceRootDirectoryNames))
                         .OrderBy(
                             document => document.FilePath,
                             StringComparer.Ordinal))
            {
                items.AddRange(
                    await getDocumentItemsAsync(
                        context.RepositoryRoot,
                        document));
            }
        }

        return [.. items];
    }

    private static async Task<ProductionSourceContext> CreateContextAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var solutionPath = Directory
            .EnumerateFiles(
                repositoryRoot,
                "*.slnx",
                SearchOption.TopDirectoryOnly)
            .Single();
        var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(solutionPath);

        return new ProductionSourceContext(
            RepositoryRoot: repositoryRoot,
            Solution: solution,
            Workspace: workspace);
    }

    private static bool IsSourceProject(
        string repositoryRoot,
        string? projectFile,
        IReadOnlyCollection<string> sourceRootDirectoryNames)
    {
        return projectFile is not null &&
               IsWithinSourceRoots(
                   repositoryRoot,
                   projectFile,
                   sourceRootDirectoryNames);
    }

    private static bool IsSourceFile(
        string repositoryRoot,
        string? sourceFile,
        IReadOnlyCollection<string> sourceRootDirectoryNames)
    {
        return sourceFile is not null &&
               IsWithinSourceRoots(
                   repositoryRoot,
                   sourceFile,
                   sourceRootDirectoryNames) &&
               !sourceFile
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

    private static bool IsWithinSourceRoots(
        string repositoryRoot,
        string path,
        IReadOnlyCollection<string> sourceRootDirectoryNames)
    {
        var fullPath = Path.GetFullPath(path);

        return sourceRootDirectoryNames.Any(sourceRootDirectoryName =>
        {
            var sourceRoot = Path.Combine(
                    repositoryRoot,
                    sourceRootDirectoryName)
                + Path.DirectorySeparatorChar;

            return fullPath.StartsWith(
                sourceRoot,
                StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(
                    directory.FullName,
                    SourceRootDirectoryName)) &&
                Directory.EnumerateFiles(
                        directory.FullName,
                        "*.slnx",
                        SearchOption.TopDirectoryOnly)
                    .Any())
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find repository root containing " +
            $"'{SourceRootDirectoryName}' and a solution file.");
    }
}

internal sealed record ProductionSourceContext(
    string RepositoryRoot,
    Solution Solution,
    MSBuildWorkspace Workspace);
