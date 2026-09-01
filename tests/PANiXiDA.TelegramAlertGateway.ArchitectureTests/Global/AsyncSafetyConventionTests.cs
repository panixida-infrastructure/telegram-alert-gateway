using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Global;

public sealed class AsyncSafetyConventionTests
{
    [Fact(DisplayName = "Blocking async calls should not be used when production code is declared")]
    public async Task BlockingAsyncCalls_Should_NotBeUsed_When_ProductionCodeIsDeclared()
    {
        var analysis = await AsyncSafetySourceDiscovery.GetAnalysisAsync();
        var violations = analysis.BlockingCalls
            .Select(call =>
                $"{call.RelativePath}:{call.LineNumber}: '{call.Expression}' " +
                $"blocks asynchronous execution and must be awaited.")
            .ToArray();

        Assert.NotEmpty(analysis.Callables);
        Assert.True(
            violations.Length == 0,
            $"Blocking async calls: {violations.Length} total." +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Async void callables should not be declared when production code is declared")]
    public async Task AsyncVoidCallables_Should_NotBeDeclared_When_ProductionCodeIsDeclared()
    {
        var analysis = await AsyncSafetySourceDiscovery.GetAnalysisAsync();
        var violations = analysis.AsyncVoidCallables
            .Select(callable =>
                $"{callable.RelativePath}:{callable.LineNumber}: " +
                $"'{callable.Expression}' is async void and cannot be " +
                $"reliably awaited or observed.")
            .ToArray();

        Assert.NotEmpty(analysis.Callables);
        Assert.True(
            violations.Length == 0,
            $"Async void callables: {violations.Length} total." +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }
}

internal static class AsyncSafetySourceDiscovery
{
    private static readonly Lazy<Task<AsyncSafetyAnalysis>> Analysis =
        new(CreateAnalysisAsync);

    internal static Task<AsyncSafetyAnalysis> GetAnalysisAsync()
    {
        return Analysis.Value;
    }

    private static async Task<AsyncSafetyAnalysis> CreateAnalysisAsync()
    {
        var documents = await ProductionSourceDocumentDiscovery
            .GetItemsAsync(GetDocumentAnalysisAsync);

        return new AsyncSafetyAnalysis(
            Callables:
            [
                .. documents
                    .SelectMany(document => document.Callables)
                    .OrderBy(
                        callable => callable.RelativePath,
                        StringComparer.Ordinal)
                    .ThenBy(callable => callable.Position)
            ],
            BlockingCalls:
            [
                .. documents
                    .SelectMany(document => document.BlockingCalls)
                    .OrderBy(
                        call => call.RelativePath,
                        StringComparer.Ordinal)
                    .ThenBy(call => call.Position)
            ],
            AsyncVoidCallables:
            [
                .. documents
                    .SelectMany(document => document.AsyncVoidCallables)
                    .OrderBy(
                        callable => callable.RelativePath,
                        StringComparer.Ordinal)
                    .ThenBy(callable => callable.Position)
            ]);
    }

    private static async Task<AsyncSafetyDocumentAnalysis[]>
        GetDocumentAnalysisAsync(
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
        var callableNodes = root
            .DescendantNodes()
            .Where(node => node is MethodDeclarationSyntax or
                LocalFunctionStatementSyntax or
                AnonymousFunctionExpressionSyntax)
            .ToArray();

        return
        [
            new AsyncSafetyDocumentAnalysis(
                Callables:
                [
                    .. callableNodes.Select(node => CreateSource(
                        relativePath,
                        node,
                        GetCallableName(node)))
                ],
                BlockingCalls:
                [
                    .. root
                        .DescendantNodes()
                        .OfType<ExpressionSyntax>()
                        .Where(expression => IsBlockingCall(
                            semanticModel,
                            expression))
                        .Select(expression => CreateSource(
                            relativePath,
                            expression,
                            expression.ToString()))
                ],
                AsyncVoidCallables:
                [
                    .. callableNodes
                        .Where(node => IsAsyncVoid(
                            semanticModel,
                            node))
                        .Select(node => CreateSource(
                            relativePath,
                            node,
                            GetCallableName(node)))
                ])
        ];
    }

    private static bool IsBlockingCall(
        SemanticModel semanticModel,
        ExpressionSyntax expression)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess &&
            semanticModel.GetSymbolInfo(memberAccess).Symbol is
                IPropertySymbol
            {
                Name: "Result"
            } property &&
            IsTaskType(property.ContainingType))
        {
            return true;
        }

        if (expression is not InvocationExpressionSyntax invocation ||
            semanticModel.GetOperation(invocation) is not
                IInvocationOperation operation)
        {
            return false;
        }

        if (string.Equals(
                operation.TargetMethod.Name,
                "Wait",
                StringComparison.Ordinal) &&
            IsTaskType(operation.TargetMethod.ContainingType))
        {
            return true;
        }

        return string.Equals(
                   operation.TargetMethod.Name,
                   "GetResult",
                   StringComparison.Ordinal) &&
               operation.TargetMethod.ContainingNamespace.ToDisplayString() ==
               "System.Runtime.CompilerServices" &&
               operation.TargetMethod.ContainingType.Name.Contains(
                   "TaskAwaiter",
                   StringComparison.Ordinal);
    }

    private static bool IsAsyncVoid(
        SemanticModel semanticModel,
        SyntaxNode callable)
    {
        return callable switch
        {
            MethodDeclarationSyntax method =>
                method.Modifiers.Any(modifier =>
                    modifier.ValueText == "async") &&
                semanticModel.GetDeclaredSymbol(method) is IMethodSymbol
                {
                    ReturnsVoid: true
                },
            LocalFunctionStatementSyntax localFunction =>
                localFunction.Modifiers.Any(modifier =>
                    modifier.ValueText == "async") &&
                semanticModel.GetDeclaredSymbol(localFunction) is IMethodSymbol
                {
                    ReturnsVoid: true
                },
            AnonymousFunctionExpressionSyntax anonymousFunction =>
                anonymousFunction.AsyncKeyword.ValueText == "async" &&
                semanticModel.GetTypeInfo(anonymousFunction).ConvertedType is
                    INamedTypeSymbol
                {
                    DelegateInvokeMethod.ReturnsVoid: true
                },
            _ => false
        };
    }

    private static bool IsTaskType(ITypeSymbol type)
    {
        return type.ContainingNamespace.ToDisplayString() ==
               "System.Threading.Tasks" &&
               type.Name is "Task" or "ValueTask";
    }

    private static AsyncSafetySource CreateSource(
        string relativePath,
        SyntaxNode node,
        string expression)
    {
        return new AsyncSafetySource(
            RelativePath: relativePath,
            LineNumber: node
                .GetLocation()
                .GetLineSpan()
                .StartLinePosition.Line + 1,
            Position: node.SpanStart,
            Expression: expression);
    }

    private static string GetCallableName(SyntaxNode callable)
    {
        return callable switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            LocalFunctionStatementSyntax localFunction =>
                localFunction.Identifier.ValueText,
            AnonymousFunctionExpressionSyntax anonymousFunction =>
                anonymousFunction.ToString(),
            _ => callable.ToString()
        };
    }
}

internal sealed record AsyncSafetyAnalysis(
    AsyncSafetySource[] Callables,
    AsyncSafetySource[] BlockingCalls,
    AsyncSafetySource[] AsyncVoidCallables);

internal sealed record AsyncSafetyDocumentAnalysis(
    AsyncSafetySource[] Callables,
    AsyncSafetySource[] BlockingCalls,
    AsyncSafetySource[] AsyncVoidCallables);

internal sealed record AsyncSafetySource(
    string RelativePath,
    int LineNumber,
    int Position,
    string Expression);
