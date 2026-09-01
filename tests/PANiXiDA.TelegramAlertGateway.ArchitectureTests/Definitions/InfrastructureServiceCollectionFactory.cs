using System.Reflection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Definitions;

internal static class InfrastructureServiceCollectionFactory
{
    internal static IServiceCollection Create(Assembly infrastructureAssembly)
    {
        var addInfrastructureMethod = infrastructureAssembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly))
            .Single(method =>
            {
                var parameters = method.GetParameters();

                return string.Equals(
                           method.Name,
                           "AddInfrastructure",
                           StringComparison.Ordinal) &&
                       parameters.Length >= 2 &&
                       parameters[0].ParameterType ==
                       typeof(IServiceCollection) &&
                       parameters[1].ParameterType ==
                       typeof(IConfiguration);
            });
        var serviceCollection = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSqlConnectionString"] =
                    "Host=localhost;Database=architecture-tests"
            })
            .Build();
        var arguments = addInfrastructureMethod
            .GetParameters()
            .Select(parameter =>
            {
                if (parameter.ParameterType == typeof(IServiceCollection))
                {
                    return serviceCollection;
                }

                if (parameter.ParameterType == typeof(IConfiguration))
                {
                    return configuration;
                }

                return parameter.HasDefaultValue
                    ? parameter.DefaultValue
                    : null;
            })
            .ToArray();

        addInfrastructureMethod.Invoke(
            obj: null,
            parameters: arguments);

        return serviceCollection;
    }
}
