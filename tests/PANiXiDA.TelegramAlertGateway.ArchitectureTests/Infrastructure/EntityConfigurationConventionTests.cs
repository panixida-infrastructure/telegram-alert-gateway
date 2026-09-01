using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Domain.AggregateRoots;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Infrastructure;

public sealed class EntityConfigurationConventionTests
{
    private const string MaximumLengthMemberName = "MaxLength";

    private static readonly string[] ExplicitStoreNamingMethods =
    [
        "HasColumnName",
        "ToTable",
        "ToView"
    ];

    [Fact(DisplayName = "Auditable entity configurations should reside in aggregate Write roots when declared")]
    public void AuditableEntityConfigurations_Should_ResideInAggregateWriteRoots_When_Declared()
    {
        var configurations = GetAuditableEntityConfigurations();
        var violations = configurations
            .SelectMany(type =>
                InfrastructurePersistenceConvention
                    .GetAggregateFeatureLocationViolations(
                        type,
                        "Write"))
            .ToArray();

        Assert.NotEmpty(configurations);
        Assert.True(
            violations.Length == 0,
            $"Auditable entity configuration location violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Entity type configurations should reside in aggregate Write roots when declared")]
    public void EntityTypeConfigurations_Should_ResideInAggregateWriteRoots_When_Declared()
    {
        var configurations = GetEntityTypeConfigurations();
        var violations = configurations
            .SelectMany(type =>
                InfrastructurePersistenceConvention
                    .GetAggregateFeatureLocationViolations(
                        type,
                        "Write"))
            .ToArray();

        Assert.NotEmpty(configurations);
        Assert.True(
            violations.Length == 0,
            $"Entity type configuration location violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Auditable entity configurations should avoid explicit store object names when declared")]
    public void AuditableEntityConfigurations_Should_AvoidExplicitStoreObjectNames_When_Declared()
    {
        var configurations = GetAuditableEntityConfigurations();
        var violations = configurations
            .SelectMany(GetExplicitStoreNamingViolations)
            .ToArray();

        Assert.NotEmpty(configurations);
        Assert.True(
            violations.Length == 0,
            $"Auditable entity configuration explicit naming violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Entity type configurations should avoid explicit store object names when declared")]
    public void EntityTypeConfigurations_Should_AvoidExplicitStoreObjectNames_When_Declared()
    {
        var configurations = GetEntityTypeConfigurations();
        var violations = configurations
            .SelectMany(GetExplicitStoreNamingViolations)
            .ToArray();

        Assert.NotEmpty(configurations);
        Assert.True(
            violations.Length == 0,
            $"Entity type configuration explicit naming violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Entity configuration length limits should reference domain constants when declared")]
    public void EntityConfigurationLengthLimits_Should_ReferenceDomainConstants_When_Declared()
    {
        var configurations = GetEntityTypeConfigurations();
        var domainTypesWithMaximumLength = GetDomainTypesWithMaximumLength();
        var violations = configurations
            .SelectMany(configuration =>
                GetLengthConstraintViolations(
                    configuration,
                    domainTypesWithMaximumLength))
            .ToArray();

        Assert.NotEmpty(configurations);
        Assert.NotEmpty(domainTypesWithMaximumLength);
        Assert.True(
            violations.Length == 0,
            $"Entity configuration length limits must reference a domain " +
            $"type's {MaximumLengthMemberName} constant:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static Type[] GetAuditableEntityConfigurations()
    {
        return InfrastructurePersistenceConvention
            .GetConcreteInfrastructureTypes(type =>
                InfrastructurePersistenceConvention
                    .GetClosedGenericBaseType(
                        type,
                        typeof(AuditableEntityConfiguration<>)) is not null);
    }

    private static Type[] GetEntityTypeConfigurations()
    {
        return InfrastructurePersistenceConvention
            .GetConcreteInfrastructureTypes(type =>
            {
                var configurationInterface = InfrastructurePersistenceConvention
                    .GetClosedGenericInterface(
                        type,
                        typeof(IEntityTypeConfiguration<>));

                return configurationInterface is not null
                       && typeof(IAggregateRoot).IsAssignableFrom(
                           configurationInterface.GetGenericArguments()[0]);
            });
    }

    private static HashSet<string> GetDomainTypesWithMaximumLength()
    {
        var productionAssemblies = ArchitectureDefinition
            .ProductionAssemblies
            .ToDictionary(
                assembly => assembly.GetName().Name
                    ?? throw new InvalidOperationException(
                        $"Could not determine the name of assembly " +
                        $"'{assembly.FullName}'."),
                StringComparer.Ordinal);

        return ArchitectureDefinition.Modules
            .SelectMany(module => productionAssemblies[
                    module.DomainAssemblyName]
                .GetTypes())
            .Where(type => type.GetField(MaximumLengthMemberName) is
            {
                IsLiteral: true,
                IsStatic: true,
                IsPublic: true
            })
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> GetLengthConstraintViolations(
        Type configuration,
        IReadOnlySet<string> domainTypesWithMaximumLength)
    {
        return InfrastructurePersistenceConvention
            .FindSourceFiles(configuration)
            .SelectMany(sourceFile =>
            {
                var syntaxRoot = CSharpSyntaxTree
                    .ParseText(File.ReadAllText(sourceFile))
                    .GetRoot();

                return syntaxRoot
                    .DescendantNodes()
                    .OfType<TypeDeclarationSyntax>()
                    .Where(declaration => string.Equals(
                        declaration.Identifier.ValueText,
                        configuration.Name,
                        StringComparison.Ordinal))
                    .SelectMany(declaration => declaration
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>())
                    .Where(invocation => string.Equals(
                        GetInvokedMethodName(invocation),
                        "HasMaxLength",
                        StringComparison.Ordinal))
                    .Where(invocation => !IsDomainMaximumLengthReference(
                        invocation,
                        domainTypesWithMaximumLength))
                    .Select(invocation =>
                        $"{configuration.FullName} calls HasMaxLength " +
                        $"without a domain {MaximumLengthMemberName} " +
                        $"constant at '{Path.GetFileName(sourceFile)}:" +
                        $"{invocation.GetLocation()
                            .GetLineSpan()
                            .StartLinePosition.Line + 1}'.");
            });
    }

    private static bool IsDomainMaximumLengthReference(
        InvocationExpressionSyntax invocation,
        IReadOnlySet<string> domainTypesWithMaximumLength)
    {
        if (invocation.ArgumentList.Arguments.SingleOrDefault()?.Expression is not
            MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: MaximumLengthMemberName
            } memberAccess)
        {
            return false;
        }

        var ownerName = memberAccess.Expression
            .DescendantNodesAndSelf()
            .OfType<SimpleNameSyntax>()
            .LastOrDefault()
            ?.Identifier.ValueText;

        return ownerName is not null &&
               domainTypesWithMaximumLength.Contains(ownerName);
    }

    private static IEnumerable<string> GetExplicitStoreNamingViolations(
        Type configuration)
    {
        return InfrastructurePersistenceConvention
            .FindSourceFiles(configuration)
            .SelectMany(sourceFile =>
            {
                var syntaxRoot = CSharpSyntaxTree
                    .ParseText(File.ReadAllText(sourceFile))
                    .GetRoot();

                return syntaxRoot
                    .DescendantNodes()
                    .OfType<TypeDeclarationSyntax>()
                    .Where(declaration => string.Equals(
                        declaration.Identifier.ValueText,
                        configuration.Name,
                        StringComparison.Ordinal))
                    .SelectMany(declaration => declaration
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>())
                    .Select(invocation => new
                    {
                        Invocation = invocation,
                        MethodName = GetInvokedMethodName(invocation)
                    })
                    .Where(candidate =>
                        candidate.MethodName is not null &&
                        ExplicitStoreNamingMethods.Contains(
                            candidate.MethodName,
                            StringComparer.Ordinal))
                    .Select(candidate =>
                        $"{configuration.FullName} explicitly calls " +
                        $"'{candidate.MethodName}' at " +
                        $"'{Path.GetFileName(sourceFile)}:" +
                        $"{candidate.Invocation.GetLocation()
                            .GetLineSpan()
                            .StartLinePosition.Line + 1}'.");
            });
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
}
