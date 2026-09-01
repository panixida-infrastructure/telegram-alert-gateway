using System.Reflection;
using System.Text.RegularExpressions;

using PANiXiDA.Core.Presentation.Http.Endpoints;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Presentation;

public sealed partial class EndpointMetadataConventionTests
{
    [GeneratedRegex(
        "^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex RouteSegmentPattern();

    [GeneratedRegex(
        "^\\{[a-z][a-z0-9]*(?::[a-z][a-z0-9]*)?\\}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex RouteParameterPattern();

    [GeneratedRegex(
        "^[A-Z][A-Za-z0-9]*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    [GeneratedRegex(
        "^[A-Z][A-Za-z0-9]*(?: [A-Za-z0-9]+)*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SummaryPattern();

    private static readonly string[] EndpointGroupMetadataPropertyNames =
    [
        "ApiVersion",
        "Name",
        "Route"
    ];

    [Fact(DisplayName = "Endpoint metadata should follow HTTP naming conventions when endpoint is declared")]
    public void EndpointMetadata_Should_FollowNamingConventions_When_EndpointIsDeclared()
    {
        var metadataTypes = ArchitectureDefinition.ProductionAssemblies
            .Where(assembly => assembly.GetName().Name?.EndsWith(
                ".Presentation",
                StringComparison.Ordinal) == true)
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Select(type => new
            {
                Type = type,
                IsEndpoint = typeof(IEndpoint).IsAssignableFrom(type),
                IsEndpointGroup = typeof(IEndpointGroup).IsAssignableFrom(type)
            })
            .Where(metadata => metadata.IsEndpoint || metadata.IsEndpointGroup)
            .OrderBy(metadata => metadata.Type.FullName, StringComparer.Ordinal)
            .ToArray();

        var violations = new List<string>();

        foreach (var metadata in metadataTypes)
        {
            var instance = Activator.CreateInstance(metadata.Type, nonPublic: true)
                ?? throw new InvalidOperationException($"Could not create '{metadata.Type.FullName}'.");
            var route = GetMetadata(instance, "Route");
            var name = GetMetadata(instance, "Name");

            if (!IsValidRoute(route))
            {
                violations.Add(
                    $"{metadata.Type.FullName}.Route must use lowercase English kebab-case: '{route}'.");
            }

            if (!NamePattern().IsMatch(name))
            {
                violations.Add(
                    $"{metadata.Type.FullName}.Name must be one English PascalCase identifier: '{name}'.");
            }

            if (!metadata.IsEndpoint)
            {
                continue;
            }

            var summary = GetMetadata(instance, "Summary");
            if (!SummaryPattern().IsMatch(summary))
            {
                violations.Add(
                    $"{metadata.Type.FullName}.Summary must be English sentence case with single spaces: '{summary}'.");
            }
        }

        Assert.True(
            violations.Count == 0,
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Endpoint group metadata properties should be get only when group is declared")]
    public void EndpointGroupMetadataProperties_Should_BeGetOnly_When_GroupIsDeclared()
    {
        var endpointGroups =
            PresentationArchitectureConvention.GetEndpointGroups();
        var violations = endpointGroups
            .SelectMany(endpointGroup =>
                EndpointGroupMetadataPropertyNames.Select(propertyName => new
                {
                    EndpointGroup = endpointGroup,
                    Property = endpointGroup.GetProperty(
                        propertyName,
                        BindingFlags.Instance | BindingFlags.Public),
                    PropertyName = propertyName
                }))
            .Where(candidate =>
                candidate.Property is null ||
                !candidate.Property.CanRead ||
                candidate.Property.SetMethod is not null)
            .Select(candidate =>
                $"{candidate.EndpointGroup.FullName}." +
                $"{candidate.PropertyName} must be a get-only public " +
                $"property.")
            .ToArray();

        Assert.NotEmpty(endpointGroups);
        Assert.True(
            violations.Length == 0,
            $"Endpoint group metadata mutability violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static string GetMetadata(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);

        return property?.GetValue(instance) as string ?? string.Empty;
    }

    private static bool IsValidRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return false;
        }

        return route
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .All(segment =>
                RouteSegmentPattern().IsMatch(segment) ||
                RouteParameterPattern().IsMatch(segment));
    }
}
