using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Infrastructure;

public sealed class DependencyInjectionConventionTests
{
    [Fact(DisplayName = "Infrastructure implementations should be registered for domain or application abstractions when declared")]
    public void InfrastructureImplementations_Should_BeRegisteredForDomainOrApplicationAbstractions_When_Declared()
    {
        var registrations = GetAbstractionRegistrations();
        var violations = registrations
            .Where(registration => !registration.ServiceDescriptors.Any(
                descriptor => IsImplementation(
                    descriptor,
                    registration.Implementation)))
            .Select(registration =>
                $"{registration.Implementation.FullName} implements " +
                $"{registration.Abstraction.FullName} and must be registered " +
                $"for that abstraction in the module service collection.")
            .ToArray();

        Assert.NotEmpty(registrations);
        Assert.True(
            violations.Length == 0,
            $"Missing Infrastructure registrations for Application or " +
            $"Domain abstractions:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Database contexts should be registered when declared")]
    public void DatabaseContexts_Should_BeRegistered_When_Declared()
    {
        var registrations = GetDatabaseContextRegistrations();
        var violations = registrations
            .Where(registration =>
                registration.ServiceDescriptors.Length == 0)
            .Select(registration =>
                $"{registration.DatabaseContext.FullName} must be " +
                $"registered in the module service collection.")
            .ToArray();

        Assert.NotEmpty(registrations);
        Assert.True(
            violations.Length == 0,
            $"Database context registration violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static AbstractionRegistration[] GetAbstractionRegistrations()
    {
        return
        [
            .. ArchitectureDefinition.Modules
                .SelectMany(module =>
                {
                    var domainAssembly = GetAssembly(
                        module.DomainAssemblyName);
                    var applicationAssembly = GetAssembly(
                        module.ApplicationAssemblyName);
                    var infrastructureAssembly = GetAssembly(
                        module.InfrastructureAssemblyName);
                    var serviceCollection =
                        InfrastructureServiceCollectionFactory.Create(
                            infrastructureAssembly);
                    var abstractions = domainAssembly
                        .GetTypes()
                        .Concat(applicationAssembly.GetTypes())
                        .Where(type => type.IsInterface)
                        .ToArray();

                    return infrastructureAssembly
                        .GetTypes()
                        .Where(type => type is
                            {
                                IsClass: true,
                                IsAbstract: false
                            })
                        .SelectMany(implementation => abstractions
                            .Where(abstraction =>
                                abstraction.IsAssignableFrom(implementation))
                            .Select(abstraction =>
                                new AbstractionRegistration(
                                    Abstraction: abstraction,
                                    Implementation: implementation,
                                    ServiceDescriptors:
                                    [
                                        .. serviceCollection.Where(
                                            descriptor =>
                                                descriptor.ServiceType ==
                                                abstraction)
                                    ])));
                })
                .OrderBy(
                    registration => registration.Abstraction.FullName,
                    StringComparer.Ordinal)
                .ThenBy(
                    registration => registration.Implementation.FullName,
                    StringComparer.Ordinal)
        ];
    }

    private static DatabaseContextRegistration[]
        GetDatabaseContextRegistrations()
    {
        return
        [
            .. ArchitectureDefinition.Modules
                .SelectMany(module =>
                {
                    var infrastructureAssembly = GetAssembly(
                        module.InfrastructureAssemblyName);
                    var serviceCollection =
                        InfrastructureServiceCollectionFactory.Create(
                            infrastructureAssembly);

                    return infrastructureAssembly
                        .GetTypes()
                        .Where(type =>
                            type is
                            {
                                IsClass: true,
                                IsAbstract: false
                            } &&
                            typeof(DbContext).IsAssignableFrom(type))
                        .Select(databaseContext =>
                            new DatabaseContextRegistration(
                                DatabaseContext: databaseContext,
                                ServiceDescriptors:
                                [
                                    .. serviceCollection.Where(descriptor =>
                                        descriptor.ServiceType ==
                                        databaseContext)
                                ]));
                })
                .OrderBy(
                    registration => registration.DatabaseContext.FullName,
                    StringComparer.Ordinal)
        ];
    }

    private static System.Reflection.Assembly GetAssembly(
        string assemblyName)
    {
        return ArchitectureDefinition.ProductionAssemblies.Single(
            assembly => string.Equals(
                assembly.GetName().Name,
                assemblyName,
                StringComparison.Ordinal));
    }

    private static bool IsImplementation(
        ServiceDescriptor descriptor,
        Type implementation)
    {
        return descriptor.ImplementationType == implementation ||
               descriptor.ImplementationInstance?.GetType() == implementation;
    }

    private sealed record AbstractionRegistration(
        Type Abstraction,
        Type Implementation,
        ServiceDescriptor[] ServiceDescriptors);

    private sealed record DatabaseContextRegistration(
        Type DatabaseContext,
        ServiceDescriptor[] ServiceDescriptors);
}
