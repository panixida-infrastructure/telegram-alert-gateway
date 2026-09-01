using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using PANiXiDA.TelegramAlertGateway.ArchitectureTests.Global;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Domain;

public sealed class DomainNullForgivingConventionTests
{
    private const string EntityInterfaceName =
        "PANiXiDA.Core.Domain.Entities.IEntity";
    private const string ValueObjectTypeName =
        "PANiXiDA.Core.Domain.ValueObject";

    [Fact(DisplayName = "Domain null-forgiving assignments should target only complex value objects when used in domain state")]
    public async Task NullForgivingAssignments_Should_TargetOnlyComplexValueObjects_When_UsedInDomainState()
    {
        var assignments = await ProductionSourceDocumentDiscovery
            .GetItemsAsync(GetNullForgivingAssignmentsAsync);
        var violations = assignments
            .Where(assignment => !assignment.IsComplexValueObject)
            .Select(assignment =>
                $"{assignment.RelativePath}:{assignment.LineNumber}: " +
                $"{assignment.Member} has type '{assignment.MemberType}' " +
                $"with {assignment.ValueCount} declared value(s); null! is " +
                $"allowed only for value objects with more than one value.")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Domain null-forgiving assignment violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static async Task<DomainNullForgivingAssignment[]>
        GetNullForgivingAssignmentsAsync(
            string repositoryRoot,
            Document document)
    {
        var syntaxRoot = await document.GetSyntaxRootAsync();
        var semanticModel = await document.GetSemanticModelAsync();
        var sourceFile = document.FilePath;

        if (syntaxRoot is null ||
            semanticModel is null ||
            sourceFile is null)
        {
            return [];
        }

        return
        [
            .. syntaxRoot
                .DescendantNodes()
                .OfType<PostfixUnaryExpressionSyntax>()
                .Where(expression =>
                    expression.IsKind(
                        SyntaxKind.SuppressNullableWarningExpression) &&
                    expression.Operand.IsKind(
                        SyntaxKind.NullLiteralExpression))
                .Select(expression => new
                {
                    Expression = expression,
                    Target = GetTargetSymbol(
                        semanticModel,
                        expression)
                })
                .Where(item =>
                    item.Target is not null &&
                    IsDomainEntity(item.Target.ContainingType))
                .Select(item => CreateAssignment(
                    repositoryRoot,
                    sourceFile,
                    item.Expression,
                    item.Target!))
        ];
    }

    private static ISymbol? GetTargetSymbol(
        SemanticModel semanticModel,
        PostfixUnaryExpressionSyntax expression)
    {
        return expression.Parent switch
        {
            AssignmentExpressionSyntax assignment
                when assignment.Right == expression =>
                semanticModel.GetSymbolInfo(assignment.Left).Symbol,
            EqualsValueClauseSyntax
            {
                Parent: PropertyDeclarationSyntax property
            } => semanticModel.GetDeclaredSymbol(property),
            EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax variable
            } => semanticModel.GetDeclaredSymbol(variable),
            _ => null
        };
    }

    private static DomainNullForgivingAssignment CreateAssignment(
        string repositoryRoot,
        string sourceFile,
        PostfixUnaryExpressionSyntax expression,
        ISymbol target)
    {
        var memberType = target switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => throw new InvalidOperationException(
                $"Unsupported null-forgiving target '{target}'.")
        };
        var valueCount = GetDeclaredValueCount(memberType);

        return new DomainNullForgivingAssignment(
            RelativePath: Path.GetRelativePath(
                repositoryRoot,
                sourceFile),
            LineNumber: expression
                .GetLocation()
                .GetLineSpan()
                .StartLinePosition.Line + 1,
            Member: $"{target.ContainingType}.{target.Name}",
            MemberType: memberType.ToDisplayString(),
            ValueCount: valueCount,
            IsComplexValueObject:
                IsValueObject(memberType) && valueCount > 1);
    }

    private static int GetDeclaredValueCount(ITypeSymbol type)
    {
        return type.GetMembers()
            .OfType<IPropertySymbol>()
            .Count(property =>
                !property.IsStatic &&
                !property.IsIndexer &&
                !property.IsImplicitlyDeclared);
    }

    private static bool IsDomainEntity(INamedTypeSymbol? type)
    {
        return type?.AllInterfaces.Any(interfaceType =>
            string.Equals(
                interfaceType.ToDisplayString(),
                EntityInterfaceName,
                StringComparison.Ordinal)) == true;
    }

    private static bool IsValueObject(ITypeSymbol type)
    {
        for (var currentType = type as INamedTypeSymbol;
             currentType is not null;
             currentType = currentType.BaseType)
        {
            if (string.Equals(
                    currentType.ToDisplayString(),
                    ValueObjectTypeName,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed record DomainNullForgivingAssignment(
    string RelativePath,
    int LineNumber,
    string Member,
    string MemberType,
    int ValueCount,
    bool IsComplexValueObject);
