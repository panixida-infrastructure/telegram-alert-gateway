using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

using PANiXiDA.Core.Presentation.Http.Endpoints;

using PANiXiDA.TelegramAlertGateway.ArchitectureTests.Global;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Presentation;

public sealed class EndpointMappingConventionTests
{
    [Fact(DisplayName = "Endpoint mappings should declare authorization intent when mapped")]
    public async Task EndpointMappings_Should_DeclareAuthorizationIntent_When_Mapped()
    {
        var mappings = await EndpointMappingSourceDiscovery.GetMappingsAsync();
        var violations = mappings
            .Where(mapping => !mapping.HasAuthorizationIntent)
            .Select(mapping =>
                $"{mapping.RelativePath}:{mapping.LineNumber}: " +
                $"{mapping.Endpoint.FullName} maps " +
                $"{string.Join(',', mapping.HttpMethods)} " +
                $"'{mapping.Route}' without RequireAuthorization() or " +
                $"AllowAnonymous() on the endpoint or its group.")
            .ToArray();

        Assert.NotEmpty(mappings);
        Assert.True(
            violations.Length == 0,
            $"Endpoint authorization intent violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Endpoint mappings should not repeat matching group authorization when authorization matches")]
    public async Task EndpointMappings_Should_NotRepeatGroupAuthorization_When_AuthorizationMatches()
    {
        var mappings = await EndpointMappingSourceDiscovery.GetMappingsAsync();
        var violations = mappings
            .SelectMany(mapping => mapping.EndpointAuthorizationDeclarations
                .Intersect(
                    mapping.GroupAuthorizationDeclarations,
                    StringComparer.Ordinal)
                .Select(authorization =>
                    $"{mapping.RelativePath}:{mapping.LineNumber}: " +
                    $"{mapping.Endpoint.FullName} repeats group " +
                    $"authorization '{authorization}'."))
            .ToArray();

        Assert.NotEmpty(mappings);
        Assert.True(
            violations.Length == 0,
            $"Duplicate endpoint group authorization:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Endpoint names should be unique when mapped")]
    public async Task EndpointNames_Should_BeUnique_When_Mapped()
    {
        var mappings = await EndpointMappingSourceDiscovery.GetMappingsAsync();
        var violations = mappings
            .GroupBy(
                mapping => mapping.Name,
                StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group =>
                $"Endpoint name '{group.Key}' is used by " +
                string.Join(
                    ", ",
                    group.Select(mapping =>
                        $"{mapping.Endpoint.FullName} " +
                        $"({string.Join(',', mapping.HttpMethods)} " +
                        $"{mapping.Route})")))
            .ToArray();

        Assert.NotEmpty(mappings);
        Assert.True(
            violations.Length == 0,
            $"Duplicate endpoint names:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Endpoint routes should be unique when mapped")]
    public async Task EndpointRoutes_Should_BeUnique_When_Mapped()
    {
        var mappings = await EndpointMappingSourceDiscovery.GetMappingsAsync();
        var routeMappings = mappings
            .SelectMany(mapping => mapping.HttpMethods.Select(httpMethod =>
                new
                {
                    Mapping = mapping,
                    Key = $"{mapping.ApiVersion}|{httpMethod}|" +
                          mapping.Route
                }))
            .ToArray();
        var violations = routeMappings
            .GroupBy(
                mapping => mapping.Key,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group =>
                $"Endpoint route '{group.Key}' is used by " +
                string.Join(
                    ", ",
                    group.Select(mapping =>
                        mapping.Mapping.Endpoint.FullName)))
            .ToArray();

        Assert.NotEmpty(mappings);
        Assert.True(
            violations.Length == 0,
            $"Duplicate endpoint routes:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }
}

internal static class EndpointMappingSourceDiscovery
{
    private const string EndpointMapBuilderTypeName =
        "PANiXiDA.Core.Presentation.Http.Endpoints.EndpointMapBuilder";
    private const string EndpointInterfaceName = "IEndpoint";
    private const string EndpointGroupInterfaceName = "IEndpointGroup";
    private const string EndpointInterfaceNamespace =
        "PANiXiDA.Core.Presentation.Http.Endpoints";
    private const string PresentationAssemblySuffix = ".Presentation";

    private static readonly Lazy<Task<EndpointMapping[]>> Mappings =
        new(CreateMappingsAsync);

    internal static Task<EndpointMapping[]> GetMappingsAsync()
    {
        return Mappings.Value;
    }

    private static async Task<EndpointMapping[]> CreateMappingsAsync()
    {
        var documents = await ProductionSourceDocumentDiscovery
            .GetItemsAsync(GetDocumentMappingsAsync);
        var groupAuthorization = documents
            .SelectMany(document => document.GroupAuthorization)
            .ToDictionary(
                authorization => authorization.TypeName,
                authorization => authorization,
                StringComparer.Ordinal);
        var endpointTypes = PresentationArchitectureConvention
            .GetEndpoints()
            .ToDictionary(
                type => type.FullName
                    ?? throw new InvalidOperationException(
                        $"Endpoint '{type}' does not have a full name."),
                StringComparer.Ordinal);

        return
        [
            .. documents
                .SelectMany(document => document.Mappings)
                .Select(mapping => CreateMapping(
                    mapping,
                    endpointTypes,
                    groupAuthorization))
                .OrderBy(
                    mapping => mapping.Endpoint.FullName,
                    StringComparer.Ordinal)
                .ThenBy(mapping => mapping.Position)
        ];
    }

    private static EndpointMapping CreateMapping(
        EndpointMappingSource source,
        Dictionary<string, Type> endpointTypes,
        IReadOnlyDictionary<string, EndpointGroupAuthorization>
            groupAuthorization)
    {
        var endpoint = endpointTypes[source.EndpointTypeName];
        var endpointGroup =
            PresentationArchitectureConvention.GetEndpointGroup(endpoint);
        var endpointInstance = (IEndpoint)(Activator.CreateInstance(
            endpoint,
            nonPublic: true)
            ?? throw new InvalidOperationException(
                $"Could not create '{endpoint.FullName}'."));
        var groupInstance = (IEndpointGroup)(Activator.CreateInstance(
            endpointGroup,
            nonPublic: true)
            ?? throw new InvalidOperationException(
                $"Could not create '{endpointGroup.FullName}'."));
        var endpointGroupName = endpointGroup.FullName
            ?? throw new InvalidOperationException(
                $"Endpoint group '{endpointGroup}' does not have a full " +
                $"name.");
        var endpointGroupAuthorization =
            groupAuthorization.GetValueOrDefault(endpointGroupName);

        return new EndpointMapping(
            Endpoint: endpoint,
            RelativePath: source.RelativePath,
            LineNumber: source.LineNumber,
            Position: source.Position,
            HttpMethods: source.HttpMethods,
            Name: source.ExplicitName ?? endpointInstance.Name,
            Route: CombineRoute(
                groupInstance.Route,
                endpointInstance.Route),
            ApiVersion: groupInstance.ApiVersion.ToString(),
            HasAuthorizationIntent:
                source.HasAuthorizationIntent ||
                endpointGroupAuthorization?.HasAuthorizationIntent == true,
            EndpointAuthorizationDeclarations:
                source.AuthorizationDeclarations,
            GroupAuthorizationDeclarations:
                endpointGroupAuthorization?.AuthorizationDeclarations ?? []);
    }

    private static async Task<EndpointMappingDocument[]>
        GetDocumentMappingsAsync(
            string repositoryRoot,
            Document document)
    {
        if (document.Project.AssemblyName?.EndsWith(
                PresentationAssemblySuffix,
                StringComparison.Ordinal) != true)
        {
            return [];
        }

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
        var typeDeclarations = root
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Select(declaration => new
            {
                Declaration = declaration,
                Symbol = semanticModel.GetDeclaredSymbol(declaration) as
                    INamedTypeSymbol
            })
            .Where(target => target.Symbol is not null)
            .ToArray();
        var mappings = typeDeclarations
            .Where(target => Implements(
                target.Symbol!,
                EndpointInterfaceName))
            .SelectMany(target => GetEndpointMappings(
                relativePath,
                semanticModel,
                target.Declaration,
                target.Symbol!))
            .ToArray();
        var groupAuthorization = typeDeclarations
            .Where(target => Implements(
                target.Symbol!,
                EndpointGroupInterfaceName))
            .Select(target => new EndpointGroupAuthorization(
                TypeName: target.Symbol!.ToDisplayString(),
                HasAuthorizationIntent: HasAuthorizationIntent(
                    semanticModel,
                    target.Declaration),
                AuthorizationDeclarations: GetAuthorizationDeclarations(
                    semanticModel,
                    target.Declaration)))
            .ToArray();

        return
        [
            new EndpointMappingDocument(
                Mappings: mappings,
                GroupAuthorization: groupAuthorization)
        ];
    }

    private static IEnumerable<EndpointMappingSource> GetEndpointMappings(
        string relativePath,
        SemanticModel semanticModel,
        TypeDeclarationSyntax declaration,
        INamedTypeSymbol endpointType)
    {
        return declaration
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => new
            {
                Invocation = invocation,
                Operation = semanticModel.GetOperation(invocation) as
                    IInvocationOperation
            })
            .Where(target =>
                target.Operation is not null &&
                target.Operation.TargetMethod.ContainingType
                    .ToDisplayString() == EndpointMapBuilderTypeName)
            .Select(target => new EndpointMappingSource(
                EndpointTypeName: endpointType.ToDisplayString(),
                RelativePath: relativePath,
                LineNumber: target.Invocation
                    .GetLocation()
                    .GetLineSpan()
                    .StartLinePosition.Line + 1,
                Position: target.Invocation.SpanStart,
                HttpMethods: GetHttpMethods(target.Operation!),
                ExplicitName: GetExplicitName(
                    semanticModel,
                    target.Invocation),
                HasAuthorizationIntent: HasAuthorizationIntent(
                    semanticModel,
                    GetMappingStatement(target.Invocation)),
                AuthorizationDeclarations: GetAuthorizationDeclarations(
                    semanticModel,
                    GetMappingStatement(target.Invocation))));
    }

    private static string[] GetHttpMethods(
        IInvocationOperation operation)
    {
        return operation.TargetMethod.Name switch
        {
            "MapGet" => ["GET"],
            "MapPost" => ["POST"],
            "MapPut" => ["PUT"],
            "MapPatch" => ["PATCH"],
            "MapDelete" => ["DELETE"],
            "MapMethods" => GetConstantStrings(
                operation.Arguments[0].Value),
            _ => []
        };
    }

    private static string[] GetConstantStrings(IOperation operation)
    {
        return
        [
            .. operation
                .DescendantsAndSelf()
                .Where(candidate =>
                    candidate.ConstantValue.HasValue &&
                    candidate.ConstantValue.Value is string)
                .Select(candidate =>
                    (string)candidate.ConstantValue.Value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static string? GetExplicitName(
        SemanticModel semanticModel,
        InvocationExpressionSyntax mappingInvocation)
    {
        return GetMappingStatement(mappingInvocation)
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => new
            {
                Invocation = invocation,
                Operation = semanticModel.GetOperation(invocation) as
                    IInvocationOperation
            })
            .Where(target =>
                target.Operation?.TargetMethod.Name == "WithName" &&
                target.Invocation != mappingInvocation &&
                target.Invocation.Span.Contains(mappingInvocation.Span))
            .Select(target => target.Invocation.ArgumentList.Arguments
                .FirstOrDefault()
                ?.Expression)
            .Where(expression => expression is not null)
            .Select(expression => semanticModel.GetConstantValue(expression!))
            .Where(value => value.HasValue && value.Value is string)
            .Select(value => (string)value.Value!)
            .LastOrDefault();
    }

    private static bool HasAuthorizationIntent(
        SemanticModel semanticModel,
        SyntaxNode node)
    {
        return GetAuthorizationDeclarations(
                semanticModel,
                node)
            .Length > 0;
    }

    private static string[] GetAuthorizationDeclarations(
        SemanticModel semanticModel,
        SyntaxNode node)
    {
        return
        [
            .. node
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => new
            {
                Invocation = invocation,
                Operation = semanticModel.GetOperation(invocation) as
                    IInvocationOperation
            })
            .Where(target => target.Operation?.TargetMethod.Name is
                "RequireAuthorization" or "AllowAnonymous")
            .Select(target =>
                $"{target.Operation!.TargetMethod.Name}(" +
                string.Join(
                    ",",
                    target.Invocation.ArgumentList.Arguments.Select(argument =>
                        argument.Expression
                            .NormalizeWhitespace()
                            .ToFullString())) +
                ")")
            .Distinct(StringComparer.Ordinal)
        ];
    }

    private static SyntaxNode GetMappingStatement(
        InvocationExpressionSyntax invocation)
    {
        var statement = invocation.Ancestors()
            .OfType<ExpressionStatementSyntax>()
            .FirstOrDefault();

        return statement is null
            ? invocation
            : statement;
    }

    private static bool Implements(
        INamedTypeSymbol type,
        string interfaceTypeName)
    {
        return type.AllInterfaces.Any(interfaceType =>
            string.Equals(
                interfaceType.Name,
                interfaceTypeName,
                StringComparison.Ordinal) &&
            string.Equals(
                interfaceType.ContainingNamespace.ToDisplayString(),
                EndpointInterfaceNamespace,
                StringComparison.Ordinal));
    }

    private static string CombineRoute(
        string groupRoute,
        string endpointRoute)
    {
        var segments = new[]
            {
                groupRoute,
                endpointRoute
            }
            .SelectMany(route => route.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries));

        return "/" + string.Join('/', segments);
    }
}

internal sealed record EndpointMapping(
    Type Endpoint,
    string RelativePath,
    int LineNumber,
    int Position,
    string[] HttpMethods,
    string Name,
    string Route,
    string ApiVersion,
    bool HasAuthorizationIntent,
    string[] EndpointAuthorizationDeclarations,
    string[] GroupAuthorizationDeclarations);

internal sealed record EndpointMappingDocument(
    EndpointMappingSource[] Mappings,
    EndpointGroupAuthorization[] GroupAuthorization);

internal sealed record EndpointMappingSource(
    string EndpointTypeName,
    string RelativePath,
    int LineNumber,
    int Position,
    string[] HttpMethods,
    string? ExplicitName,
    bool HasAuthorizationIntent,
    string[] AuthorizationDeclarations);

internal sealed record EndpointGroupAuthorization(
    string TypeName,
    bool HasAuthorizationIntent,
    string[] AuthorizationDeclarations);
