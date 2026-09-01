namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Definitions;

internal sealed record ModuleArchitecture(
    string Name,
    string ContractsAssemblyName,
    string DomainAssemblyName,
    string ApplicationAssemblyName,
    string InfrastructureAssemblyName,
    string PresentationAssemblyName);
