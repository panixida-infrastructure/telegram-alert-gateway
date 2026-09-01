using System.Reflection;
using System.Runtime.CompilerServices;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Global;

public sealed class AsyncMethodNamingConventionTests
{
    private const string AsyncSuffix = "Async";

    [Fact(DisplayName = "Asynchronous methods should end with Async when declared")]
    public void AsynchronousMethods_Should_EndWithAsync_When_Declared()
    {
        var methods = GetProductionMethods();
        var violations = methods
            .Where(IsAsynchronous)
            .Where(method => !method.Name.EndsWith(
                AsyncSuffix,
                StringComparison.Ordinal))
            .Select(method =>
                $"{GetDisplayName(method)} is asynchronous and must end " +
                $"with '{AsyncSuffix}'.")
            .ToArray();

        Assert.NotEmpty(methods);
        Assert.True(
            violations.Length == 0,
            $"Asynchronous method naming violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Synchronous methods should not end with Async when declared")]
    public void SynchronousMethods_Should_NotEndWithAsync_When_Declared()
    {
        var methods = GetProductionMethods();
        var violations = methods
            .Where(method => !IsAsynchronous(method))
            .Where(method => method.Name.EndsWith(
                AsyncSuffix,
                StringComparison.Ordinal))
            .Select(method =>
                $"{GetDisplayName(method)} is synchronous and must not end " +
                $"with '{AsyncSuffix}'.")
            .ToArray();

        Assert.NotEmpty(methods);
        Assert.True(
            violations.Length == 0,
            $"Synchronous method naming violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static MethodInfo[] GetProductionMethods()
    {
        return
        [
            .. ArchitectureDefinition.ProductionAssemblies
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type =>
                    !type.IsDefined(
                        typeof(CompilerGeneratedAttribute),
                        inherit: false))
                .SelectMany(type => type.GetMethods(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.DeclaredOnly))
                .Where(method =>
                    !method.IsSpecialName &&
                    !method.Name.StartsWith(
                        '<') &&
                    !method.IsDefined(
                        typeof(CompilerGeneratedAttribute),
                        inherit: false) &&
                    !HasExternallyDefinedName(method))
                .OrderBy(
                    method => method.DeclaringType?.FullName,
                    StringComparer.Ordinal)
                .ThenBy(method => method.Name, StringComparer.Ordinal)
        ];
    }

    private static bool HasExternallyDefinedName(MethodInfo method)
    {
        if (method.GetBaseDefinition() != method)
        {
            return true;
        }

        var declaringType = method.DeclaringType;

        if (declaringType is null || declaringType.IsInterface)
        {
            return false;
        }

        return declaringType.GetInterfaces().Any(interfaceType =>
        {
            var interfaceMapping =
                declaringType.GetInterfaceMap(interfaceType);

            return interfaceMapping.TargetMethods.Contains(method);
        });
    }

    private static bool IsAsynchronous(MethodInfo method)
    {
        return method.IsDefined(
                   typeof(AsyncStateMachineAttribute),
                   inherit: false) ||
               IsAsyncReturnType(method.ReturnType);
    }

    private static bool IsAsyncReturnType(Type returnType)
    {
        if (typeof(Task).IsAssignableFrom(returnType) ||
            returnType == typeof(ValueTask))
        {
            return true;
        }

        if (returnType.IsGenericType &&
            returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            return true;
        }

        return returnType
            .GetInterfaces()
            .Append(returnType)
            .Any(type =>
                type.IsGenericType &&
                type.GetGenericTypeDefinition() is var definition &&
                (definition == typeof(IAsyncEnumerable<>) ||
                 definition == typeof(IAsyncEnumerator<>)));
    }

    private static string GetDisplayName(MethodInfo method)
    {
        return $"{method.DeclaringType?.FullName}.{method.Name}";
    }
}
