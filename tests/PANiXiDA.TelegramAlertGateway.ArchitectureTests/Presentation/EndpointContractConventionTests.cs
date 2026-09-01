using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Presentation;

public sealed class EndpointContractConventionTests
{
    private const string EndpointSuffix = "Endpoint";
    private const string MapperSuffix = "Mapper";
    private const string RequestSuffix = "Request";
    private const string ResponseSuffix = "Response";

    [Fact(DisplayName = "Mapperly mappers should end with Mapper when declared")]
    public void MapperlyMappers_Should_EndWithMapper_When_Declared()
    {
        var mappers = PresentationArchitectureConvention
            .GetPresentationTypes(
                PresentationArchitectureConvention.IsMapperlyMapper);
        var violations = mappers
            .Where(mapper => !mapper.Name.EndsWith(
                MapperSuffix,
                StringComparison.Ordinal))
            .Select(mapper =>
                $"{mapper.FullName} must end with '{MapperSuffix}'.")
            .ToArray();

        Assert.NotEmpty(mappers);
        Assert.True(
            violations.Length == 0,
            $"Mapperly mapper naming violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Endpoint input types should end with Request when declared")]
    public void EndpointInputTypes_Should_EndWithRequest_When_Declared()
    {
        var endpointInputs = GetEndpointParts()
            .SelectMany(parts => parts.Inputs)
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        var violations = endpointInputs
            .Where(type => !type.Name.EndsWith(
                RequestSuffix,
                StringComparison.Ordinal))
            .Select(type =>
                $"{type.FullName} is accepted by an endpoint and must end " +
                $"with '{RequestSuffix}'.")
            .ToArray();

        Assert.NotEmpty(endpointInputs);
        Assert.True(
            violations.Length == 0,
            $"Endpoint input naming violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Endpoint output types should end with Response when declared")]
    public void EndpointOutputTypes_Should_EndWithResponse_When_Declared()
    {
        var endpointOutputs = GetEndpointParts()
            .SelectMany(parts => parts.Outputs)
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        var violations = endpointOutputs
            .Where(type => !type.Name.EndsWith(
                ResponseSuffix,
                StringComparison.Ordinal))
            .Select(type =>
                $"{type.FullName} is returned by an endpoint and must end " +
                $"with '{ResponseSuffix}'.")
            .ToArray();

        Assert.NotEmpty(endpointOutputs);
        Assert.True(
            violations.Length == 0,
            $"Endpoint output naming violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Endpoint slice parts should share one feature folder when declared")]
    public void EndpointSliceParts_Should_ShareOneFeatureFolder_When_Declared()
    {
        var endpointParts = GetEndpointParts();
        var violations = endpointParts
            .SelectMany(GetColocationViolations)
            .ToArray();

        Assert.NotEmpty(endpointParts);
        Assert.True(
            violations.Length == 0,
            $"Endpoint slice colocation violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static EndpointParts[] GetEndpointParts()
    {
        return
        [
            .. PresentationArchitectureConvention
                .GetEndpoints()
                .Select(endpoint =>
                {
                    var mappers = GetReferencedMappers(endpoint);

                    return new EndpointParts(
                        Endpoint: endpoint,
                        Inputs: GetEndpointInputTypes(endpoint),
                        Outputs: GetEndpointOutputTypes(endpoint),
                        Mappers: mappers);
                })
        ];
    }

    private static Type[] GetEndpointInputTypes(Type endpoint)
    {
        var handlerInputTypes = endpoint
            .GetMethods(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Where(type =>
                type.Assembly == endpoint.Assembly &&
                string.Equals(
                    type.Namespace,
                    endpoint.Namespace,
                    StringComparison.Ordinal));
        var acceptedTypes = GetGenericInvocationTypes(
            endpoint,
            "Accepts");

        return
        [
            .. handlerInputTypes
                .Concat(acceptedTypes)
                .Distinct()
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
        ];
    }

    private static Type[] GetEndpointOutputTypes(Type endpoint)
    {
        return GetGenericInvocationTypes(
            endpoint,
            "Produces");
    }

    private static Type[] GetGenericInvocationTypes(
        Type endpoint,
        string methodName)
    {
        return
        [
            .. PresentationArchitectureConvention
                .GetSourceSyntax(endpoint)
                .SelectMany(source => source.Root
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>())
                .Select(invocation => invocation.Expression)
                .OfType<MemberAccessExpressionSyntax>()
                .Select(memberAccess => memberAccess.Name)
                .OfType<GenericNameSyntax>()
                .Where(genericName => string.Equals(
                    genericName.Identifier.ValueText,
                    methodName,
                    StringComparison.Ordinal))
                .SelectMany(genericName =>
                    genericName.TypeArgumentList.Arguments)
                .SelectMany(typeSyntax => typeSyntax
                    .DescendantNodesAndSelf()
                    .OfType<SimpleNameSyntax>())
                .Select(typeName =>
                    PresentationArchitectureConvention
                        .ResolvePresentationType(
                            endpoint,
                            typeName.Identifier.ValueText))
                .Where(type => type is not null)
                .Cast<Type>()
                .Distinct()
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
        ];
    }

    private static Type[] GetReferencedMappers(Type endpoint)
    {
        return
        [
            .. PresentationArchitectureConvention
                .GetSourceSyntax(endpoint)
                .SelectMany(source => source.Root
                    .DescendantNodes()
                    .OfType<IdentifierNameSyntax>())
                .Where(identifier => identifier.Identifier.ValueText.EndsWith(
                    MapperSuffix,
                    StringComparison.Ordinal))
                .Select(identifier =>
                    PresentationArchitectureConvention
                        .ResolvePresentationType(
                            endpoint,
                            identifier.Identifier.ValueText))
                .Where(type => type is not null)
                .Cast<Type>()
                .Distinct()
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
        ];
    }

    private static IEnumerable<string> GetColocationViolations(
        EndpointParts parts)
    {
        var endpointNamespace = parts.Endpoint.Namespace
            ?? throw new InvalidOperationException(
                $"Endpoint '{parts.Endpoint.FullName}' does not have a " +
                $"namespace.");
        var endpointSourceDirectories =
            PresentationArchitectureConvention
                .FindSourceFiles(parts.Endpoint)
                .Select(Path.GetDirectoryName)
                .Where(directory => directory is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        var sliceParts = parts.Inputs
            .Concat(parts.Outputs)
            .Concat(parts.Mappers)
            .Distinct()
            .ToArray();
        var violations = sliceParts
            .Where(type => !string.Equals(
                type.Namespace,
                endpointNamespace,
                StringComparison.Ordinal))
            .Select(type =>
                $"{type.FullName} must share namespace " +
                $"'{endpointNamespace}' with " +
                $"{parts.Endpoint.Name}.")
            .ToList();

        violations.AddRange(sliceParts
            .SelectMany(type =>
                PresentationArchitectureConvention
                    .FindSourceFiles(type)
                    .Where(sourceFile =>
                        !endpointSourceDirectories.Contains(
                            Path.GetDirectoryName(sourceFile),
                            StringComparer.OrdinalIgnoreCase))
                    .Select(sourceFile =>
                        $"{type.FullName} source file " +
                        $"'{sourceFile}' must share the feature directory " +
                        $"with {parts.Endpoint.FullName}.")));

        return violations;
    }

    private sealed record EndpointParts(
        Type Endpoint,
        Type[] Inputs,
        Type[] Outputs,
        Type[] Mappers);
}
