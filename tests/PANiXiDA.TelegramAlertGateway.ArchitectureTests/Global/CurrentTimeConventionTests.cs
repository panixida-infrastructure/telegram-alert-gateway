using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Global;

public sealed class CurrentTimeConventionTests
{
    [Fact(DisplayName = "Current time access should use UTC sources when declared")]
    public async Task CurrentTimeAccess_Should_UseUtcSources_When_Declared()
    {
        var accesses = await CurrentTimeSourceDiscovery.GetAccessesAsync();
        var violations = accesses
            .Where(access => !access.IsAllowed)
            .Select(access =>
                $"{access.RelativePath}:{access.LineNumber}: " +
                $"'{access.Expression}' must use TimeProvider.GetUtcNow(). " +
                $"DateTime/DateTimeOffset.UtcNow is allowed only in " +
                $"constructors.")
            .ToArray();

        Assert.NotEmpty(accesses);
        Assert.True(
            violations.Length == 0,
            $"Current time access violations: {violations.Length} total." +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }
}

internal static class CurrentTimeSourceDiscovery
{
    private static readonly string[] DateTimeProperties =
    [
        "Now",
        "Today",
        "UtcNow"
    ];

    private static readonly string[] TimeProviderMethods =
    [
        "GetLocalNow",
        "GetUtcNow"
    ];

    internal static async Task<CurrentTimeAccess[]> GetAccessesAsync()
    {
        var accesses = await ProductionSourceDocumentDiscovery
            .GetItemsAsync(GetDocumentAccessesAsync);

        return
        [
            .. accesses
                .Distinct()
                .OrderBy(
                    access => access.RelativePath,
                    StringComparer.Ordinal)
                .ThenBy(access => access.Position)
        ];
    }

    private static async Task<CurrentTimeAccess[]>
        GetDocumentAccessesAsync(
            string repositoryRoot,
            Document document)
    {
        var root = await document.GetSyntaxRootAsync();
        var semanticModel = await document.GetSemanticModelAsync();
        var sourceFile = document.FilePath;

        if (root is null ||
            semanticModel is null ||
            sourceFile is null)
        {
            return [];
        }

        var relativePath = Path.GetRelativePath(
            repositoryRoot,
            sourceFile);

        return
        [
            .. root
                .DescendantNodes()
                .Select(node => GetAccess(
                    relativePath,
                    semanticModel,
                    node))
                .OfType<CurrentTimeAccess>()
        ];
    }

    private static CurrentTimeAccess? GetAccess(
        string relativePath,
        SemanticModel semanticModel,
        SyntaxNode node)
    {
        return node switch
        {
            MemberAccessExpressionSyntax memberAccess =>
                GetDateTimeAccess(
                    relativePath,
                    semanticModel,
                    memberAccess),
            InvocationExpressionSyntax invocation =>
                GetTimeProviderAccess(
                    relativePath,
                    semanticModel,
                    invocation),
            _ => null
        };
    }

    private static CurrentTimeAccess? GetDateTimeAccess(
        string relativePath,
        SemanticModel semanticModel,
        MemberAccessExpressionSyntax memberAccess)
    {
        if (semanticModel.GetSymbolInfo(memberAccess).Symbol is not
            IPropertySymbol property ||
            !DateTimeProperties.Contains(
                property.Name,
                StringComparer.Ordinal) ||
            !IsDateTimeType(property.ContainingType))
        {
            return null;
        }

        return CreateAccess(
            relativePath,
            memberAccess,
            isAllowed:
                string.Equals(
                    property.Name,
                    "UtcNow",
                    StringComparison.Ordinal) &&
                memberAccess.Ancestors()
                    .OfType<ConstructorDeclarationSyntax>()
                    .Any());
    }

    private static CurrentTimeAccess? GetTimeProviderAccess(
        string relativePath,
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation)
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not
            IMethodSymbol method ||
            !TimeProviderMethods.Contains(
                method.Name,
                StringComparer.Ordinal) ||
            !IsType(
                method.ContainingType,
                "System.TimeProvider"))
        {
            return null;
        }

        return CreateAccess(
            relativePath,
            invocation,
            isAllowed: string.Equals(
                method.Name,
                "GetUtcNow",
                StringComparison.Ordinal));
    }

    private static CurrentTimeAccess CreateAccess(
        string relativePath,
        ExpressionSyntax expression,
        bool isAllowed)
    {
        return new CurrentTimeAccess(
            RelativePath: relativePath,
            LineNumber: expression
                .GetLocation()
                .GetLineSpan()
                .StartLinePosition.Line + 1,
            Position: expression.SpanStart,
            Expression: expression.ToString(),
            IsAllowed: isAllowed);
    }

    private static bool IsDateTimeType(ITypeSymbol type)
    {
        return IsType(type, "System.DateTime") ||
               IsType(type, "System.DateTimeOffset");
    }

    private static bool IsType(
        ITypeSymbol type,
        string expectedType)
    {
        return string.Equals(
            type.ToDisplayString(),
            expectedType,
            StringComparison.Ordinal);
    }
}

internal sealed record CurrentTimeAccess(
    string RelativePath,
    int LineNumber,
    int Position,
    string Expression,
    bool IsAllowed);
