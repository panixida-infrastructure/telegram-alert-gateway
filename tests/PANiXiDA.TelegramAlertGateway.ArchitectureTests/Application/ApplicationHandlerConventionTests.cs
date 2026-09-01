using FluentValidation;

using PANiXiDA.Core.Application.Messaging.EventBus.Handlers;
using PANiXiDA.Core.Application.Messaging.Mediator.Handlers;

using PANiXiDA.TelegramAlertGateway.ArchitectureTests.Tests;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Application;

public sealed class ApplicationHandlerConventionTests
{
    private const string ApplicationDirectoryName = "Application";
    private const string SourceDirectoryName = "src";
    private const string TestsDirectoryName = "tests";
    private const string UnitTestsAssemblySuffix = ".UnitTests";

    private static readonly Type[] HandlerInterfaceDefinitions =
    [
        typeof(ICommandHandler<,>),
        typeof(IQueryHandler<,>),
        typeof(IEventHandler<>)
    ];

    private static readonly Type[] RequestHandlerInterfaceDefinitions =
    [
        typeof(ICommandHandler<,>),
        typeof(IQueryHandler<,>)
    ];

    [Fact(DisplayName = "Application handlers should have matching unit test files when handlers are declared")]
    public void ApplicationHandlers_Should_HaveMatchingUnitTestFiles_When_HandlersAreDeclared()
    {
        var repositoryRoot = FindRepositoryRoot();
        var handlers = GetApplicationHandlers(repositoryRoot);
        var testMethods = TestSourceDiscovery.GetTestMethods();

        Assert.NotEmpty(handlers);

        var violations = handlers
            .Where(handler =>
                !File.Exists(handler.ExpectedTestFilePath) ||
                !testMethods.Any(testMethod => HasPath(
                    repositoryRoot,
                    testMethod,
                    handler.ExpectedTestFilePath)))
            .Select(handler =>
                $"{handler.Type.FullName} must have a separate unit test " +
                $"file containing at least one Fact or Theory at " +
                $"'{Path.GetRelativePath(
                    repositoryRoot,
                    handler.ExpectedTestFilePath)}'.")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Missing or empty Application handler unit test files:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Application handler unit tests should cover every handler method when handlers are declared")]
    public void ApplicationHandlerUnitTests_Should_CoverEveryHandlerMethod_When_HandlersAreDeclared()
    {
        var repositoryRoot = FindRepositoryRoot();
        var handlers = GetApplicationHandlers(repositoryRoot);
        var testMethods = TestSourceDiscovery.GetTestMethods();

        Assert.NotEmpty(handlers);

        var violations = handlers
            .Where(handler => File.Exists(handler.ExpectedTestFilePath))
            .SelectMany(handler => GetMissingHandlerTestMethods(
                repositoryRoot,
                handler,
                testMethods))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Missing Application handler unit test methods:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Command and query handlers should have validators when handlers are declared")]
    public void CommandAndQueryHandlers_Should_HaveValidators_When_HandlersAreDeclared()
    {
        var handlers = GetApplicationHandlers(FindRepositoryRoot());
        var validators = GetApplicationValidators();

        Assert.NotEmpty(handlers);

        var violations = handlers
            .SelectMany(handler => handler.HandlerInterfaces
                .Where(IsRequestHandlerInterface)
                .Select(handlerInterface => new
                {
                    Handler = handler.Type,
                    Request = handlerInterface.GetGenericArguments()[0]
                }))
            .Where(target => !validators.Any(validator =>
                validator.ValidatedTypes.Contains(target.Request)))
            .Select(target =>
                $"{target.Handler.FullName} must have an IValidator<" +
                $"{target.Request.FullName}> implementation.")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Missing command or query validators:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Application validators should have matching unit test files when validators are declared")]
    public void ApplicationValidators_Should_HaveMatchingUnitTestFiles_When_ValidatorsAreDeclared()
    {
        var repositoryRoot = FindRepositoryRoot();
        var validators = GetApplicationValidators();
        var testMethods = TestSourceDiscovery.GetTestMethods();

        Assert.NotEmpty(validators);

        var violations = validators
            .Select(validator => new
            {
                validator.Type,
                ExpectedTestFilePath = GetExpectedTestFilePath(
                    repositoryRoot,
                    validator.Module,
                    validator.Type)
            })
            .Where(validator =>
                !File.Exists(validator.ExpectedTestFilePath) ||
                !testMethods.Any(testMethod => HasPath(
                    repositoryRoot,
                    testMethod,
                    validator.ExpectedTestFilePath)))
            .Select(validator =>
                $"{validator.Type.FullName} must have a separate unit test " +
                $"file containing at least one Fact or Theory at " +
                $"'{Path.GetRelativePath(
                    repositoryRoot,
                    validator.ExpectedTestFilePath)}'.")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Missing or empty Application validator unit test files:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static ApplicationHandler[] GetApplicationHandlers(
        string repositoryRoot)
    {
        return
        [
            .. GetApplicationTypes()
                .Select(target => new ApplicationHandler(
                    Type: target.Type,
                    HandlerInterfaces:
                    [
                        .. target.Type
                            .GetInterfaces()
                            .Where(IsHandlerInterface)
                    ],
                    ExpectedTestFilePath: GetExpectedTestFilePath(
                        repositoryRoot,
                        target.Module,
                        target.Type)))
                .Where(handler => handler.HandlerInterfaces.Count > 0)
                .OrderBy(handler => handler.Type.FullName, StringComparer.Ordinal)
        ];
    }

    private static ApplicationValidator[] GetApplicationValidators()
    {
        return
        [
            .. GetApplicationTypes()
                .Select(target => new ApplicationValidator(
                    Type: target.Type,
                    ValidatedTypes:
                    [
                        .. target.Type
                            .GetInterfaces()
                            .Where(IsValidatorInterface)
                            .Select(validatorInterface =>
                                validatorInterface.GetGenericArguments()[0])
                    ],
                    Module: target.Module))
                .Where(validator => validator.ValidatedTypes.Count > 0)
                .OrderBy(validator => validator.Type.FullName, StringComparer.Ordinal)
        ];
    }

    private static IEnumerable<ApplicationType> GetApplicationTypes()
    {
        var productionAssemblies = ArchitectureDefinition.ProductionAssemblies
            .ToDictionary(
                assembly => assembly.GetName().Name
                    ?? throw new InvalidOperationException(
                        $"Could not determine the name of assembly '{assembly.FullName}'."),
                StringComparer.Ordinal);

        return ArchitectureDefinition.Modules
            .SelectMany(module => productionAssemblies[module.ApplicationAssemblyName]
                .GetTypes()
                .Where(type =>
                    type is
                    {
                        IsClass: true,
                        IsAbstract: false,
                        Namespace: not null
                    })
                .Select(type => new ApplicationType(
                    Module: module,
                    Type: type)));
    }

    private static IEnumerable<string> GetMissingHandlerTestMethods(
        string repositoryRoot,
        ApplicationHandler handler,
        IReadOnlyCollection<TestMethodSource> testMethods)
    {
        var testMethodNames = testMethods
            .Where(testMethod => HasPath(
                repositoryRoot,
                testMethod,
                handler.ExpectedTestFilePath))
            .Select(testMethod => testMethod.Name)
            .ToArray();
        var handlerMethods = handler.HandlerInterfaces
            .SelectMany(handlerInterface =>
                handlerInterface.GetInterfaces().Append(handlerInterface))
            .SelectMany(handlerInterface => handlerInterface.GetMethods())
            .Where(method => !method.IsSpecialName)
            .Distinct()
            .GroupBy(method => method.Name, StringComparer.Ordinal);

        return handlerMethods
            .Select(methods => new
            {
                MethodName = methods.Key,
                RequiredTestCount = methods.Count(),
                ActualTestCount = testMethodNames.Count(testMethodName =>
                    testMethodName.StartsWith(
                        methods.Key + "_Should_",
                        StringComparison.Ordinal))
            })
            .Where(coverage =>
                coverage.ActualTestCount < coverage.RequiredTestCount)
            .Select(coverage =>
                $"{handler.Type.FullName}.{coverage.MethodName}: expected " +
                $"{coverage.RequiredTestCount} test method(s) starting with " +
                $"'{coverage.MethodName}_Should_', found " +
                $"{coverage.ActualTestCount} in " +
                $"'{Path.GetRelativePath(
                    repositoryRoot,
                    handler.ExpectedTestFilePath)}'.");
    }

    private static string GetExpectedTestFilePath(
        string repositoryRoot,
        ModuleArchitecture module,
        Type applicationType)
    {
        var relativeNamespace = GetRelativeNamespace(
            applicationType,
            module.ApplicationAssemblyName);
        var moduleDirectoryName =
            module.Name[(module.Name.LastIndexOf('.') + 1)..];
        var unitTestsAssemblyName =
            module.Name + UnitTestsAssemblySuffix;
        var namespacePath = relativeNamespace.Replace(
            '.',
            Path.DirectorySeparatorChar);
        var typeName = applicationType.Name.Split('`')[0];

        return Path.Combine(
            repositoryRoot,
            TestsDirectoryName,
            moduleDirectoryName,
            unitTestsAssemblyName,
            ApplicationDirectoryName,
            namespacePath,
            $"{typeName}Tests.cs");
    }

    private static string GetRelativeNamespace(
        Type type,
        string applicationAssemblyName)
    {
        var typeNamespace = type.Namespace
            ?? throw new InvalidOperationException(
                $"Application type '{type.FullName}' does not have a namespace.");
        var applicationNamespacePrefix = applicationAssemblyName + ".";

        if (string.Equals(
                typeNamespace,
                applicationAssemblyName,
                StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (typeNamespace.StartsWith(
                applicationNamespacePrefix,
                StringComparison.Ordinal))
        {
            return typeNamespace[applicationNamespacePrefix.Length..];
        }

        throw new InvalidOperationException(
            $"Application type namespace '{typeNamespace}' must be " +
            $"'{applicationAssemblyName}' or start with " +
            $"'{applicationNamespacePrefix}'.");
    }

    private static bool IsHandlerInterface(Type interfaceType)
    {
        return interfaceType.IsGenericType &&
               HandlerInterfaceDefinitions.Contains(
                   interfaceType.GetGenericTypeDefinition());
    }

    private static bool IsRequestHandlerInterface(Type interfaceType)
    {
        return interfaceType.IsGenericType &&
               RequestHandlerInterfaceDefinitions.Contains(
                   interfaceType.GetGenericTypeDefinition());
    }

    private static bool IsValidatorInterface(Type interfaceType)
    {
        return interfaceType.IsGenericType &&
               interfaceType.GetGenericTypeDefinition() ==
               typeof(IValidator<>);
    }

    private static bool HasPath(
        string repositoryRoot,
        TestMethodSource testMethod,
        string expectedPath)
    {
        var testMethodPath = Path.GetFullPath(
            Path.Combine(
                repositoryRoot,
                testMethod.RelativePath));

        return string.Equals(
            testMethodPath,
            expectedPath,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(
                    directory.FullName,
                    SourceDirectoryName)) &&
                Directory.Exists(Path.Combine(
                    directory.FullName,
                    TestsDirectoryName)))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find repository root containing " +
            $"'{SourceDirectoryName}' and '{TestsDirectoryName}' directories.");
    }

    private sealed record ApplicationType(
        ModuleArchitecture Module,
        Type Type);

    private sealed record ApplicationHandler(
        Type Type,
        IReadOnlyCollection<Type> HandlerInterfaces,
        string ExpectedTestFilePath);

    private sealed record ApplicationValidator(
        Type Type,
        IReadOnlyCollection<Type> ValidatedTypes,
        ModuleArchitecture Module);
}
