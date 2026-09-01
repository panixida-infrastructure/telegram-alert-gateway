using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using PANiXiDA.TelegramAlertGateway.ArchitectureTests.Global;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Infrastructure;

public sealed class ILikeConventionTests
{
    [Fact(DisplayName = "ILIKE calls should use named substring arguments when declared")]
    public async Task ILikeCalls_Should_UseNamedSubstringArguments_When_Declared()
    {
        var calls = await ILikeSourceDiscovery.GetCallsAsync();
        var violations = calls
            .Where(call => !call.IsValid)
            .Select(call =>
                $"{call.RelativePath}:{call.LineNumber}: '{call.Expression}' " +
                $"must use named matchExpression and pattern arguments with " +
                $"a $\"%{{value.Trim()}}%\" pattern.")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"ILIKE convention violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "ILIKE calls should rely on SQL null semantics when declared")]
    public async Task ILikeCalls_Should_NotUseExplicitNullGuards_When_Declared()
    {
        var calls = await ILikeSourceDiscovery.GetCallsAsync();
        var violations = calls
            .Where(call => call.HasRedundantNullGuard)
            .Select(call =>
                $"{call.RelativePath}:{call.LineNumber}: '{call.Expression}' " +
                "must not use an explicit null guard for matchExpression.")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"ILIKE null guard violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }
}

internal static class ILikeSourceDiscovery
{
    internal static async Task<ILikeCall[]> GetCallsAsync()
    {
        var calls = await ProductionSourceDocumentDiscovery
            .GetItemsAsync(GetDocumentCallsAsync);

        return
        [
            .. calls
                .Distinct()
                .OrderBy(
                    call => call.RelativePath,
                    StringComparer.Ordinal)
                .ThenBy(call => call.Position)
        ];
    }

    private static async Task<ILikeCall[]> GetDocumentCallsAsync(
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
                .OfType<InvocationExpressionSyntax>()
                .Select(invocation => GetCall(
                    relativePath,
                    semanticModel,
                    invocation))
                .OfType<ILikeCall>()
        ];
    }

    private static ILikeCall? GetCall(
        string relativePath,
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation)
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not
                IMethodSymbol method ||
            !string.Equals(
                method.Name,
                "ILike",
                StringComparison.Ordinal) ||
            !string.Equals(
                method.ContainingType.Name,
                "NpgsqlDbFunctionsExtensions",
                StringComparison.Ordinal))
        {
            return null;
        }

        return new ILikeCall(
            RelativePath: relativePath,
            LineNumber: invocation
                .GetLocation()
                .GetLineSpan()
                .StartLinePosition.Line + 1,
            Position: invocation.SpanStart,
            Expression: invocation.ToString(),
            IsValid: UsesNamedSubstringArguments(
                invocation.ArgumentList.Arguments),
            HasRedundantNullGuard: HasRedundantNullGuard(
                invocation,
                semanticModel));
    }

    private static bool HasRedundantNullGuard(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        var arguments = invocation.ArgumentList.Arguments;

        if (arguments.Count == 0)
        {
            return false;
        }

        var matchExpressionSymbol = semanticModel
            .GetSymbolInfo(arguments[0].Expression)
            .Symbol;

        if (matchExpressionSymbol is null)
        {
            return false;
        }

        return invocation
            .Ancestors()
            .OfType<BinaryExpressionSyntax>()
            .Where(expression =>
                expression.IsKind(SyntaxKind.LogicalAndExpression))
            .SelectMany(expression =>
                expression.DescendantNodesAndSelf())
            .Any(node => IsNullGuard(
                node,
                matchExpressionSymbol,
                semanticModel));
    }

    private static bool IsNullGuard(
        SyntaxNode node,
        ISymbol matchExpressionSymbol,
        SemanticModel semanticModel)
    {
        if (node is BinaryExpressionSyntax comparison &&
            comparison.IsKind(SyntaxKind.NotEqualsExpression))
        {
            return IsNullComparison(
                       comparison.Left,
                       comparison.Right,
                       matchExpressionSymbol,
                       semanticModel) ||
                   IsNullComparison(
                       comparison.Right,
                       comparison.Left,
                       matchExpressionSymbol,
                       semanticModel);
        }

        return node is IsPatternExpressionSyntax patternExpression &&
               patternExpression.Pattern.IsKind(SyntaxKind.NotPattern) &&
               patternExpression.Pattern
                   .DescendantNodesAndSelf()
                   .OfType<ConstantPatternSyntax>()
                   .Any(pattern =>
                       pattern.Expression.IsKind(
                           SyntaxKind.NullLiteralExpression)) &&
               RefersToMatchExpression(
                   patternExpression.Expression,
                   matchExpressionSymbol,
                   semanticModel);
    }

    private static bool IsNullComparison(
        ExpressionSyntax nullExpression,
        ExpressionSyntax candidateExpression,
        ISymbol matchExpressionSymbol,
        SemanticModel semanticModel)
    {
        return nullExpression.IsKind(SyntaxKind.NullLiteralExpression) &&
               RefersToMatchExpression(
                   candidateExpression,
                   matchExpressionSymbol,
                   semanticModel);
    }

    private static bool RefersToMatchExpression(
        ExpressionSyntax expression,
        ISymbol matchExpressionSymbol,
        SemanticModel semanticModel)
    {
        var expressionSymbol = semanticModel
            .GetSymbolInfo(expression)
            .Symbol;

        return SymbolEqualityComparer.Default.Equals(
            expressionSymbol,
            matchExpressionSymbol);
    }

    private static bool UsesNamedSubstringArguments(
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        return arguments.Count == 2 &&
               IsNamed(arguments[0], "matchExpression") &&
               IsNamed(arguments[1], "pattern") &&
               IsSubstringPattern(arguments[1].Expression);
    }

    private static bool IsNamed(
        ArgumentSyntax argument,
        string expectedName)
    {
        return string.Equals(
            argument.NameColon?.Name.Identifier.ValueText,
            expectedName,
            StringComparison.Ordinal);
    }

    private static bool IsSubstringPattern(ExpressionSyntax expression)
    {
        if (expression is not InterpolatedStringExpressionSyntax pattern ||
            pattern.Contents.Count != 3 ||
            pattern.Contents[0] is not
                InterpolatedStringTextSyntax prefix ||
            pattern.Contents[1] is not
                InterpolationSyntax interpolation ||
            pattern.Contents[2] is not
                InterpolatedStringTextSyntax suffix ||
            interpolation.Expression is not
                InvocationExpressionSyntax trimInvocation ||
            trimInvocation.Expression is not
                MemberAccessExpressionSyntax trimMemberAccess)
        {
            return false;
        }

        return string.Equals(
                   prefix.TextToken.ValueText,
                   "%",
                   StringComparison.Ordinal) &&
               string.Equals(
                   suffix.TextToken.ValueText,
                   "%",
                   StringComparison.Ordinal) &&
               string.Equals(
                   trimMemberAccess.Name.Identifier.ValueText,
                   "Trim",
                   StringComparison.Ordinal) &&
               trimInvocation.ArgumentList.Arguments.Count == 0 &&
               interpolation.AlignmentClause is null &&
               interpolation.FormatClause is null;
    }
}

internal sealed record ILikeCall(
    string RelativePath,
    int LineNumber,
    int Position,
    string Expression,
    bool IsValid,
    bool HasRedundantNullGuard);
