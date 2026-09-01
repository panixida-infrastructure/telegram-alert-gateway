using ArchUnitNET.Domain;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Layers;

public sealed class LayerDependencyTests
{
    [Fact(DisplayName = "Domain layer should not depend on outer layers when validated")]
    public void DomainLayer_Should_NotDependOnOuterLayers_When_Validated()
    {
        var forbiddenDependencies = new[]
        {
            ArchitectureDefinition.ContractsLayer,
            ArchitectureDefinition.ApplicationLayer,
            ArchitectureDefinition.InfrastructureLayer,
            ArchitectureDefinition.PresentationLayer,
            ArchitectureDefinition.HostLayer
        };

        foreach (var forbiddenDependency in forbiddenDependencies)
        {
            TypesShouldNotDependOn(
                ArchitectureDefinition.DomainLayer,
                forbiddenDependency);
        }
    }

    [Fact(DisplayName = "Contracts layer should not depend on module layers or host when validated")]
    public void ContractsLayer_Should_NotDependOnModuleLayersOrHost_When_Validated()
    {
        var forbiddenDependencies = new[]
        {
            ArchitectureDefinition.DomainLayer,
            ArchitectureDefinition.ApplicationLayer,
            ArchitectureDefinition.InfrastructureLayer,
            ArchitectureDefinition.PresentationLayer,
            ArchitectureDefinition.HostLayer
        };

        foreach (var forbiddenDependency in forbiddenDependencies)
        {
            TypesShouldNotDependOn(
                ArchitectureDefinition.ContractsLayer,
                forbiddenDependency);
        }
    }

    [Fact(DisplayName = "Application layer should depend only on domain and shared abstractions when validated")]
    public void ApplicationLayer_Should_DependOnlyOnDomainAndSharedAbstractions_When_Validated()
    {
        var forbiddenDependencies = new[]
        {
            ArchitectureDefinition.InfrastructureLayer,
            ArchitectureDefinition.PresentationLayer,
            ArchitectureDefinition.HostLayer
        };

        foreach (var forbiddenDependency in forbiddenDependencies)
        {
            TypesShouldNotDependOn(
                ArchitectureDefinition.ApplicationLayer,
                forbiddenDependency);
        }
    }

    [Fact(DisplayName = "Infrastructure layer should not depend on presentation or host when validated")]
    public void InfrastructureLayer_Should_NotDependOnPresentationOrHost_When_Validated()
    {
        var forbiddenDependencies = new[]
        {
            ArchitectureDefinition.PresentationLayer,
            ArchitectureDefinition.HostLayer
        };

        foreach (var forbiddenDependency in forbiddenDependencies)
        {
            TypesShouldNotDependOn(
                ArchitectureDefinition.InfrastructureLayer,
                forbiddenDependency);
        }
    }

    [Fact(DisplayName = "Presentation layer should not depend on domain infrastructure or host when validated")]
    public void PresentationLayer_Should_NotDependOnDomainInfrastructureOrHost_When_Validated()
    {
        var forbiddenDependencies = new[]
        {
            ArchitectureDefinition.DomainLayer,
            ArchitectureDefinition.InfrastructureLayer,
            ArchitectureDefinition.HostLayer
        };

        foreach (var forbiddenDependency in forbiddenDependencies)
        {
            TypesShouldNotDependOn(
                ArchitectureDefinition.PresentationLayer,
                forbiddenDependency);
        }
    }

    private static void TypesShouldNotDependOn(
        IObjectProvider<IType> source,
        IObjectProvider<IType> forbiddenDependency)
    {
        Types().That().Are(source)
            .Should().NotDependOnAny(forbiddenDependency)
            .WithoutRequiringPositiveResults()
            .Check(ArchitectureDefinition.Architecture);
    }
}
