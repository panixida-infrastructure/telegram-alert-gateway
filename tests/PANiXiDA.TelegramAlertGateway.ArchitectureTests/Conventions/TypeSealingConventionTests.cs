using PANiXiDA.Core.Application.Messaging.EventBus.Handlers;
using PANiXiDA.Core.Application.Messaging.Mediator.Handlers;
using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Domain.Abstractions;
using PANiXiDA.Core.Presentation.Http.Endpoints;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Conventions;

public sealed class TypeSealingConventionTests
{
    private const string ApplicationAssemblySuffix = ".Application";
    private const string DomainAssemblySuffix = ".Domain";
    private const string InfrastructureAssemblySuffix = ".Infrastructure";
    private const string PresentationAssemblySuffix = ".Presentation";

    private static readonly Type[] HandlerInterfaceDefinitions =
    [
        typeof(ICommandHandler<,>),
        typeof(IQueryHandler<,>),
        typeof(IEventHandler<>)
    ];

    private static readonly Type[] RepositoryInterfaceDefinitions =
    [
        typeof(IReadRepository<>),
        typeof(IRepository<,>)
    ];

    [Fact(DisplayName = "Concrete domain classes should be sealed when declared")]
    public void ConcreteDomainClasses_Should_BeSealed_When_Declared()
    {
        var domainClasses = GetTypesFromAssemblies(
            DomainAssemblySuffix,
            type => type is { IsClass: true, IsAbstract: false });

        AssertTypesAreSealed(
            domainClasses,
            "Concrete domain classes");
    }

    [Fact(DisplayName = "Application handlers should be sealed when declared")]
    public void ApplicationHandlers_Should_BeSealed_When_Declared()
    {
        var applicationHandlers = GetTypesFromAssemblies(
            ApplicationAssemblySuffix,
            type =>
                type is { IsClass: true, IsAbstract: false } &&
                type.GetInterfaces().Any(IsHandlerInterface));

        AssertTypesAreSealed(
            applicationHandlers,
            "Application handlers");
    }

    [Fact(DisplayName = "Repository implementations should be sealed when declared")]
    public void RepositoryImplementations_Should_BeSealed_When_Declared()
    {
        var repositoryImplementations = GetTypesFromAssemblies(
            InfrastructureAssemblySuffix,
            type =>
                type is { IsClass: true, IsAbstract: false } &&
                type.GetInterfaces().Any(IsRepositoryInterface));

        AssertTypesAreSealed(
            repositoryImplementations,
            "Repository implementations");
    }

    [Fact(DisplayName = "Endpoints and groups should be sealed when declared")]
    public void EndpointsAndGroups_Should_BeSealed_When_Declared()
    {
        var endpointTypes = GetTypesFromAssemblies(
            PresentationAssemblySuffix,
            type =>
                type is { IsClass: true, IsAbstract: false } &&
                (typeof(IEndpoint).IsAssignableFrom(type) ||
                 typeof(IEndpointGroup).IsAssignableFrom(type)));

        AssertTypesAreSealed(
            endpointTypes,
            "Endpoints and endpoint groups");
    }

    private static Type[] GetTypesFromAssemblies(
        string assemblySuffix,
        Func<Type, bool> predicate)
    {
        return
        [
            .. ArchitectureDefinition.ProductionAssemblies
                .Where(assembly => assembly.GetName().Name?.EndsWith(
                    assemblySuffix,
                    StringComparison.Ordinal) == true)
                .SelectMany(assembly => assembly.GetTypes())
                .Where(predicate)
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
        ];
    }

    private static bool IsHandlerInterface(Type interfaceType)
    {
        return interfaceType.IsGenericType &&
               HandlerInterfaceDefinitions.Contains(
                   interfaceType.GetGenericTypeDefinition());
    }

    private static bool IsRepositoryInterface(Type interfaceType)
    {
        return interfaceType.IsGenericType &&
               RepositoryInterfaceDefinitions.Contains(
                   interfaceType.GetGenericTypeDefinition());
    }

    private static void AssertTypesAreSealed(
        IReadOnlyCollection<Type> types,
        string description)
    {
        var violations = types
            .Where(type => !type.IsSealed)
            .Select(type =>
                $"{type.FullName} must be sealed.")
            .ToArray();

        Assert.NotEmpty(types);
        Assert.True(
            violations.Length == 0,
            $"{description} that are not sealed:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }
}
