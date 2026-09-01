using PANiXiDA.Core.Application.Messaging.Mediator.Handlers;
using PANiXiDA.Core.Application.Querying;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Application;

public sealed class ReadModelConventionTests
{
    private const string ApplicationAssemblySuffix = ".Application";
    private const string ReadModelSuffix = "ReadModel";

    [Fact(DisplayName = "Read models should end with ReadModel when declared")]
    public void ReadModels_Should_EndWithReadModel_When_Declared()
    {
        var readModels = GetApplicationTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false } &&
                typeof(IReadModel).IsAssignableFrom(type))
            .ToArray();
        var violations = readModels
            .Where(type => !type.Name.EndsWith(
                ReadModelSuffix,
                StringComparison.Ordinal))
            .Select(type =>
                $"{type.FullName} implements IReadModel and must end with " +
                $"'{ReadModelSuffix}'.")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Read model naming violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Types ending with ReadModel should implement IReadModel when declared")]
    public void TypesEndingWithReadModel_Should_ImplementIReadModel_When_Declared()
    {
        var namedReadModels = GetApplicationTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false } &&
                type.Name.EndsWith(
                    ReadModelSuffix,
                    StringComparison.Ordinal))
            .ToArray();
        var violations = namedReadModels
            .Where(type => !typeof(IReadModel).IsAssignableFrom(type))
            .Select(type =>
                $"{type.FullName} ends with '{ReadModelSuffix}' and must " +
                $"implement IReadModel.")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Read model inheritance violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Query handlers should return read models when declared")]
    public void QueryHandlers_Should_ReturnReadModels_When_Declared()
    {
        var queryHandlers = GetApplicationTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type
                .GetInterfaces()
                .Where(candidate =>
                    candidate.IsGenericType &&
                    candidate.GetGenericTypeDefinition() ==
                    typeof(IQueryHandler<,>))
                .Select(contract => new QueryHandler(
                    type,
                    contract.GetGenericArguments()[1])))
            .ToArray();
        var violations = queryHandlers
            .Where(handler => !ReadSideConvention.IsReadModelResult(
                handler.ResultType))
            .Select(handler =>
                $"{handler.Type.FullName} must return an IReadModel, " +
                $"optionally wrapped in Task, Result, a collection, or a " +
                $"pagination model; found '{handler.ResultType}'.")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Query handler result type violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static Type[] GetApplicationTypes()
    {
        return
        [
            .. ArchitectureDefinition.ProductionAssemblies
                .Where(assembly => assembly.GetName().Name?.EndsWith(
                    ApplicationAssemblySuffix,
                    StringComparison.Ordinal) == true)
                .SelectMany(assembly => assembly.GetTypes())
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
        ];
    }

    private sealed record QueryHandler(
        Type Type,
        Type ResultType);
}
