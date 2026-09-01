using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Application;

public sealed class DomainValidationConventionTests
{
    private const string SourceDirectoryName = "src";

    private static readonly string[] DirectDomainConstraintMethods =
    [
        "CreditCard",
        "Custom",
        "CustomAsync",
        "EmailAddress",
        "Equal",
        "ExclusiveBetween",
        "GreaterThan",
        "GreaterThanOrEqualTo",
        "InclusiveBetween",
        "IsEnumName",
        "IsInEnum",
        "Length",
        "LessThan",
        "LessThanOrEqualTo",
        "Matches",
        "MaximumLength",
        "MinimumLength",
        "Must",
        "MustAsync",
        "NotEqual",
        "PrecisionScale",
        "ScalePrecision"
    ];

    [Fact(DisplayName = "Command validators should use domain factories when domain constraints are declared")]
    public void CommandValidators_Should_UseDomainFactories_When_DomainConstraintsAreDeclared()
    {
        var repositoryRoot = FindRepositoryRoot();
        var validatorSourceFiles = Directory
            .EnumerateFiles(
                Path.Combine(repositoryRoot, SourceDirectoryName),
                "*CommandValidator.cs",
                SearchOption.AllDirectories)
            .Where(IsProductionSourceFile)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var violations = validatorSourceFiles
            .SelectMany(sourceFile => GetDirectDomainConstraintViolations(
                repositoryRoot,
                sourceFile))
            .ToArray();

        Assert.NotEmpty(validatorSourceFiles);
        Assert.True(
            violations.Length == 0,
            $"Command validators must delegate business constraints to " +
            $"domain factories through domain result validation extensions:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> GetDirectDomainConstraintViolations(
        string repositoryRoot,
        string sourceFile)
    {
        var syntaxRoot = CSharpSyntaxTree
            .ParseText(File.ReadAllText(sourceFile))
            .GetRoot();

        return syntaxRoot
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => new
            {
                Invocation = invocation,
                MethodName = GetInvokedMethodName(invocation)
            })
            .Where(candidate =>
                candidate.MethodName is not null &&
                DirectDomainConstraintMethods.Contains(
                    candidate.MethodName,
                    StringComparer.Ordinal))
            .Select(candidate =>
                $"{Path.GetRelativePath(repositoryRoot, sourceFile)}:" +
                $"{candidate.Invocation.GetLocation()
                    .GetLineSpan()
                    .StartLinePosition.Line + 1} calls " +
                $"'{candidate.MethodName}'.");
    }

    private static string? GetInvokedMethodName(
        InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess =>
                memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier =>
                identifier.Identifier.ValueText,
            GenericNameSyntax genericName =>
                genericName.Identifier.ValueText,
            _ => null
        };
    }

    private static bool IsProductionSourceFile(string path)
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
