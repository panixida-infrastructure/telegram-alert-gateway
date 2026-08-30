using ArchUnitNET.Domain;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Layers;

public sealed class LayerDependencyTests
{
    [Fact(DisplayName = "Domain layer should not depend on outer layers")]
    public void DomainLayer_Should_Not_Depend_On_Outer_Layers()
    {
        TypesShouldNotDependOn(ArchitectureDefinition.DomainLayer, ArchitectureDefinition.ApplicationLayer);
        TypesShouldNotDependOn(ArchitectureDefinition.DomainLayer, ArchitectureDefinition.InfrastructureLayer);
        TypesShouldNotDependOn(ArchitectureDefinition.DomainLayer, ArchitectureDefinition.PresentationLayer);
        TypesShouldNotDependOn(ArchitectureDefinition.DomainLayer, ArchitectureDefinition.HostLayer);
    }

    [Fact(DisplayName = "Application layer should depend only on domain and shared abstractions")]
    public void ApplicationLayer_Should_Depend_Only_On_Domain_And_Shared_Abstractions()
    {
        TypesShouldNotDependOn(ArchitectureDefinition.ApplicationLayer, ArchitectureDefinition.InfrastructureLayer);
        TypesShouldNotDependOn(ArchitectureDefinition.ApplicationLayer, ArchitectureDefinition.PresentationLayer);
        TypesShouldNotDependOn(ArchitectureDefinition.ApplicationLayer, ArchitectureDefinition.HostLayer);
    }

    [Fact(DisplayName = "Infrastructure layer should not depend on presentation or host")]
    public void InfrastructureLayer_Should_Not_Depend_On_Presentation_Or_Host()
    {
        TypesShouldNotDependOn(ArchitectureDefinition.InfrastructureLayer, ArchitectureDefinition.PresentationLayer);
        TypesShouldNotDependOn(ArchitectureDefinition.InfrastructureLayer, ArchitectureDefinition.HostLayer);
    }

    [Fact(DisplayName = "Presentation layer should not depend on domain infrastructure or host")]
    public void PresentationLayer_Should_Not_Depend_On_Domain_Infrastructure_Or_Host()
    {
        TypesShouldNotDependOn(ArchitectureDefinition.PresentationLayer, ArchitectureDefinition.DomainLayer);
        TypesShouldNotDependOn(ArchitectureDefinition.PresentationLayer, ArchitectureDefinition.InfrastructureLayer);
        TypesShouldNotDependOn(ArchitectureDefinition.PresentationLayer, ArchitectureDefinition.HostLayer);
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
