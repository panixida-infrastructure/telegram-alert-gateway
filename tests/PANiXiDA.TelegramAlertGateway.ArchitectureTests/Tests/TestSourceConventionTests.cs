using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Tests;

public sealed partial class TestSourceConventionTests
{
    [GeneratedRegex(
        @"^[A-Z][A-Za-z0-9]*_Should_[A-Z][A-Za-z0-9]*_When_[A-Z][A-Za-z0-9]*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TestMethodNamePattern();

    [Fact(DisplayName = "Test methods should use MethodName Should Behavior When Condition naming when a test is declared")]
    public void TestMethods_Should_FollowNamingConvention_When_ATestIsDeclared()
    {
        var violations = TestSourceDiscovery.GetTestMethods()
            .Where(testMethod =>
                !TestMethodNamePattern().IsMatch(testMethod.Name))
            .Select(testMethod =>
                $"{testMethod.Location}: {testMethod.Name}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Test method names must match " +
            $"'MethodName_Should_DoSomething_When_Condition':" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Test methods should follow Arrange Act Assert structure when a test is declared")]
    public void TestMethods_Should_FollowArrangeActAssert_When_ATestIsDeclared()
    {
        var violations = TestSourceDiscovery.GetTestMethods()
            .Select(GetArrangeActAssertViolation)
            .OfType<string>()
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Test methods must separate setup and execution from a final " +
            $"assertion section with a blank line:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static string? GetArrangeActAssertViolation(
        TestMethodSource testMethod)
    {
        var body = testMethod.Declaration.Body;

        if (body is null)
        {
            return $"{testMethod.Location}: {testMethod.Name} must use a block body.";
        }

        var sections = GetSections(body);
        const int minimumSectionCount = 2;

        if (sections.Count < minimumSectionCount)
        {
            return $"{testMethod.Location}: {testMethod.Name} has " +
                $"{sections.Count} logical section(s), expected at least " +
                $"{minimumSectionCount}.";
        }

        if (!sections[^1].Any(ContainsAssertion))
        {
            return $"{testMethod.Location}: {testMethod.Name} must keep " +
                $"assertions in its final logical section.";
        }

        return null;
    }

    private static List<IReadOnlyList<StatementSyntax>> GetSections(
        BlockSyntax body)
    {
        if (body.Statements.Count == 0)
        {
            return [];
        }

        var sections = new List<IReadOnlyList<StatementSyntax>>();
        var sourceText = body.SyntaxTree.GetText();
        var currentSection = new List<StatementSyntax>
        {
            body.Statements[0]
        };

        for (var index = 1; index < body.Statements.Count; index++)
        {
            var previousStatement = body.Statements[index - 1];
            var currentStatement = body.Statements[index];
            var separator = sourceText.ToString(
                TextSpan.FromBounds(
                    previousStatement.Span.End,
                    currentStatement.SpanStart));

            if (separator.Count(character => character == '\n') >= 2)
            {
                sections.Add(currentSection);
                currentSection = [];
            }

            currentSection.Add(currentStatement);
        }

        sections.Add(currentSection);

        return sections;
    }

    private static bool ContainsAssertion(StatementSyntax statement)
    {
        return statement
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(IsAssertion);
    }

    private static bool IsAssertion(InvocationExpressionSyntax invocation)
    {
        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess =>
                memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier =>
                identifier.Identifier.ValueText,
            _ => string.Empty
        };

        var containingType = invocation.Expression is
            MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax containingIdentifier
        }
            ? containingIdentifier.Identifier.ValueText
            : string.Empty;

        return name.Contains("Should", StringComparison.Ordinal) ||
               name.StartsWith("Assert", StringComparison.Ordinal) ||
               string.Equals(containingType, "Assert", StringComparison.Ordinal) ||
               string.Equals(name, "Check", StringComparison.Ordinal) ||
               string.Equals(name, "Received", StringComparison.Ordinal) ||
               string.Equals(name, "DidNotReceive", StringComparison.Ordinal);
    }
}

internal static class TestSourceDiscovery
{
    private const string SourceDirectoryName = "src";
    private const string TestsDirectoryName = "tests";

    public static TestMethodSource[] GetTestMethods()
    {
        var repositoryRoot = FindRepositoryRoot();
        var testsRoot = Path.Combine(repositoryRoot, TestsDirectoryName);

        return
        [
            .. Directory
                .EnumerateFiles(
                    testsRoot,
                    "*.cs",
                    SearchOption.AllDirectories)
                .Where(IsSourceFile)
                .OrderBy(path => path, StringComparer.Ordinal)
                .SelectMany(path =>
                    GetTestMethods(repositoryRoot, path))
        ];
    }

    private static IEnumerable<TestMethodSource> GetTestMethods(
        string repositoryRoot,
        string path)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(File.ReadAllText(path));
        var root = syntaxTree.GetRoot();

        return root
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Select(method => new
            {
                Method = method,
                TestAttribute = method.AttributeLists
                    .SelectMany(attributeList => attributeList.Attributes)
                    .FirstOrDefault(IsTestAttribute)
            })
            .Where(item => item.TestAttribute is not null)
            .Select(item => new TestMethodSource(
                RelativePath: Path.GetRelativePath(repositoryRoot, path),
                Declaration: item.Method,
                TestAttribute: item.TestAttribute!));
    }

    private static bool IsTestAttribute(AttributeSyntax attribute)
    {
        var name = attribute.Name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualifiedName => qualifiedName.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax aliasQualifiedName =>
                aliasQualifiedName.Name.Identifier.ValueText,
            _ => attribute.Name.ToString()
        };

        return name is "Fact" or "FactAttribute" or "Theory" or "TheoryAttribute";
    }

    private static bool IsSourceFile(string path)
    {
        return !path
            .Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment =>
                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
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

internal sealed record TestMethodSource(
    string RelativePath,
    MethodDeclarationSyntax Declaration,
    AttributeSyntax TestAttribute)
{
    public string Name => Declaration.Identifier.ValueText;

    public string? DisplayName
    {
        get
        {
            var expression = TestAttribute
                .ArgumentList?
                .Arguments
                .FirstOrDefault(argument =>
                    string.Equals(
                        argument.NameEquals?.Name.Identifier.ValueText,
                        "DisplayName",
                        StringComparison.Ordinal))
                ?.Expression;

            return expression is LiteralExpressionSyntax literalExpression &&
                   literalExpression.IsKind(SyntaxKind.StringLiteralExpression)
                ? literalExpression.Token.ValueText
                : null;
        }
    }

    public string Location
    {
        get
        {
            var lineNumber = Declaration
                .GetLocation()
                .GetLineSpan()
                .StartLinePosition
                .Line + 1;

            return $"{RelativePath}:{lineNumber}";
        }
    }
}
