using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Modules;

public sealed class ModuleDependencyTests
{
    [Fact(DisplayName = "Module layers should not depend on other module internals when validated")]
    public void ModuleLayers_Should_NotDependOnOtherModuleInternals_When_Validated()
    {
        var modules = ArchitectureDefinition.Modules;

        if (modules.Count < 2)
        {
            return;
        }

        foreach (var sourceModule in modules)
        {
            foreach (var targetModule in modules.Where(module =>
                         module != sourceModule))
            {
                foreach (var sourceAssemblyName in GetInternalAssemblyNames(
                             sourceModule))
                {
                    foreach (var targetAssemblyName in GetInternalAssemblyNames(
                                 targetModule))
                    {
                        TypesShouldNotDependOn(
                            sourceAssemblyName,
                            targetAssemblyName);
                    }
                }
            }
        }
    }

    private static IReadOnlyCollection<string> GetInternalAssemblyNames(
        ModuleArchitecture module)
    {
        return
        [
            module.DomainAssemblyName,
            module.ApplicationAssemblyName,
            module.InfrastructureAssemblyName,
            module.PresentationAssemblyName
        ];
    }

    private static void TypesShouldNotDependOn(
        string sourceAssemblyName,
        string targetAssemblyName)
    {
        Types()
            .That()
            .Are(ArchitectureDefinition.TypesInAssembly(sourceAssemblyName))
            .Should()
            .NotDependOnAny(
                ArchitectureDefinition.TypesInAssembly(targetAssemblyName))
            .WithoutRequiringPositiveResults()
            .Check(ArchitectureDefinition.Architecture);
    }
}
