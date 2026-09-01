using FluentValidation;

using PANiXiDA.Core.Application.Messaging.Mediator.Contracts;
using PANiXiDA.Core.Application.Messaging.Mediator.Handlers;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Application;

public sealed class ApplicationVerticalSliceConventionTests
{
    private const string AbstractionsNamespaceSegment = "Abstractions";
    private const string ApplicationAssemblySuffix = ".Application";
    private const string CommandSuffix = "Command";
    private const string DomainAssemblySuffix = ".Domain";
    private const string HandlerSuffix = "Handler";
    private const string QuerySuffix = "Query";
    private const string SourceDirectoryName = "src";
    private const string ValidatorSuffix = "Validator";

    private static readonly Type[] HandlerInterfaceDefinitions =
    [
        typeof(ICommandHandler<,>),
        typeof(IQueryHandler<,>)
    ];

    private static readonly Type[] RequestInterfaceDefinitions =
    [
        typeof(ICommand<>),
        typeof(IQuery<>)
    ];

    [Fact(DisplayName = "Application use cases should reside in feature folders when declared")]
    public void ApplicationUseCases_Should_ResideInFeatureFolders_When_Declared()
    {
        var useCases = GetApplicationUseCases();
        var violations = useCases
            .SelectMany(GetUseCaseLocationViolations)
            .ToArray();

        Assert.NotEmpty(useCases);
        Assert.True(
            violations.Length == 0,
            $"Application use case location violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Application use case types should have expected role suffixes when declared")]
    public void ApplicationUseCaseTypes_Should_HaveExpectedRoleSuffixes_When_Declared()
    {
        var useCases = GetApplicationUseCases();
        var applicationTypes = GetApplicationTypes()
            .Select(target => target.Type)
            .ToArray();
        var violations = useCases
            .SelectMany(useCase => GetUseCaseRoleSuffixViolations(
                useCase,
                applicationTypes))
            .ToArray();

        Assert.NotEmpty(useCases);
        Assert.True(
            violations.Length == 0,
            $"Application use case role suffix violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Application use case parts should share one feature folder when declared")]
    public void ApplicationUseCaseParts_Should_ShareOneFeatureFolder_When_Declared()
    {
        var repositoryRoot = FindRepositoryRoot();
        var useCases = GetApplicationUseCases();
        var applicationTypes = GetApplicationTypes()
            .Select(target => target.Type)
            .ToArray();
        var violations = useCases
            .SelectMany(useCase => GetUseCaseColocationViolations(
                repositoryRoot,
                useCase,
                applicationTypes))
            .ToArray();

        Assert.NotEmpty(useCases);
        Assert.True(
            violations.Length == 0,
            $"Application use case colocation violations:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Abstractions namespaces should contain only abstractions when declared")]
    public void AbstractionsNamespaces_Should_ContainOnlyAbstractions_When_Declared()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstractionTypes = ArchitectureDefinition.ProductionAssemblies
            .Where(assembly =>
                IsLayerAssembly(assembly, DomainAssemblySuffix) ||
                IsLayerAssembly(assembly, ApplicationAssemblySuffix))
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => HasNamespaceSegment(
                type,
                AbstractionsNamespaceSegment))
            .ToArray();
        var violations = abstractionTypes
            .Where(type =>
                !type.IsInterface &&
                !type.IsAbstract &&
                !typeof(Delegate).IsAssignableFrom(type))
            .Select(type =>
                $"{type.FullName} is concrete and must not be declared in " +
                $"an '{AbstractionsNamespaceSegment}' namespace.")
            .Concat(abstractionTypes
                .Where(type => !File.Exists(GetExpectedSourceFilePath(
                    repositoryRoot,
                    GetModule(type),
                    type)))
                .Select(type =>
                    $"{type.FullName} must have a matching source file in " +
                    $"its '{AbstractionsNamespaceSegment}' directory."))
            .Concat(GetAbstractionSourceFiles(repositoryRoot)
                .Where(sourceFile => !abstractionTypes.Any(type =>
                    string.Equals(
                        GetExpectedSourceFilePath(
                            repositoryRoot,
                            GetModule(type),
                            type),
                        sourceFile,
                        StringComparison.OrdinalIgnoreCase)))
                .Select(sourceFile =>
                    $"'{Path.GetRelativePath(repositoryRoot, sourceFile)}' " +
                    $"must declare only an abstraction in an " +
                    $"'{AbstractionsNamespaceSegment}' namespace."))
            .ToArray();

        Assert.NotEmpty(abstractionTypes);
        Assert.True(
            violations.Length == 0,
            $"Abstractions namespace violations:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> GetUseCaseLocationViolations(
        ApplicationUseCase useCase)
    {
        var violations = new List<string>();

        if (useCase.RelativeNamespaceSegments.Length < 2)
        {
            violations.Add(
                $"{useCase.RequestType.FullName} must reside in a concrete " +
                $"feature folder below at least one grouping folder.");
        }

        return violations;
    }

    private static List<string> GetUseCaseRoleSuffixViolations(
        ApplicationUseCase useCase,
        IReadOnlyCollection<Type> applicationTypes)
    {
        var violations = new List<string>();
        var expectedRequestSuffix = GetRequestSuffix(useCase.RequestType);

        if (!useCase.RequestType.Name.EndsWith(
                expectedRequestSuffix,
                StringComparison.Ordinal))
        {
            violations.Add(
                $"{useCase.RequestType.FullName} must end with " +
                $"'{expectedRequestSuffix}'.");
        }

        violations.AddRange(applicationTypes
            .Where(type => GetHandlerRequestTypes(type)
                .Contains(useCase.RequestType))
            .Where(type => !type.Name.EndsWith(
                HandlerSuffix,
                StringComparison.Ordinal))
            .Select(type =>
                $"{type.FullName} must end with '{HandlerSuffix}'."));
        violations.AddRange(applicationTypes
            .Where(type => GetValidatedTypes(type)
                .Contains(useCase.RequestType))
            .Where(type => !type.Name.EndsWith(
                ValidatorSuffix,
                StringComparison.Ordinal))
            .Select(type =>
                $"{type.FullName} must end with '{ValidatorSuffix}'."));

        return violations;
    }

    private static List<string> GetUseCaseColocationViolations(
        string repositoryRoot,
        ApplicationUseCase useCase,
        IReadOnlyCollection<Type> applicationTypes)
    {
        var handlers = applicationTypes
            .Where(type => GetHandlerRequestTypes(type)
                .Contains(useCase.RequestType))
            .ToArray();
        var validators = applicationTypes
            .Where(type => GetValidatedTypes(type)
                .Contains(useCase.RequestType))
            .ToArray();
        var violations = new List<string>();

        if (handlers.Length != 1)
        {
            violations.Add(
                $"{useCase.RequestType.FullName} must have exactly one " +
                $"handler, found: {FormatTypes(handlers)}.");
        }

        if (validators.Length != 1)
        {
            violations.Add(
                $"{useCase.RequestType.FullName} must have exactly one " +
                $"validator, found: {FormatTypes(validators)}.");
        }

        var requestSuffix = GetRequestSuffix(useCase.RequestType);
        var requestStem =
            useCase.RequestType.Name[..^requestSuffix.Length];
        var expectedHandlerName = requestStem + HandlerSuffix;
        var expectedValidatorName =
            useCase.RequestType.Name + ValidatorSuffix;

        violations.AddRange(handlers
            .Where(handler =>
                !string.Equals(
                    handler.Name,
                    expectedHandlerName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    handler.Namespace,
                    useCase.RequestType.Namespace,
                    StringComparison.Ordinal))
            .Select(handler =>
                $"{handler.FullName} must be named '{expectedHandlerName}' " +
                $"and share namespace '{useCase.RequestType.Namespace}' with " +
                $"{useCase.RequestType.Name}."));
        violations.AddRange(validators
            .Where(validator =>
                !string.Equals(
                    validator.Name,
                    expectedValidatorName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    validator.Namespace,
                    useCase.RequestType.Namespace,
                    StringComparison.Ordinal))
            .Select(validator =>
                $"{validator.FullName} must be named " +
                $"'{expectedValidatorName}' and share namespace " +
                $"'{useCase.RequestType.Namespace}' with " +
                $"{useCase.RequestType.Name}."));

        var colocatedTypes = handlers
            .Concat(validators)
            .Append(useCase.RequestType)
            .ToArray();
        violations.AddRange(colocatedTypes
            .Where(type => !File.Exists(GetExpectedSourceFilePath(
                repositoryRoot,
                useCase.Module,
                type)))
            .Select(type =>
                $"{type.FullName} must have a matching source file in its " +
                $"Application feature folder."));

        return violations;
    }

    private static ApplicationUseCase[] GetApplicationUseCases()
    {
        return
        [
            .. ArchitectureDefinition.Modules
                .SelectMany(module =>
                {
                    var applicationAssembly =
                        ArchitectureDefinition.ProductionAssemblies.Single(
                            assembly => string.Equals(
                                assembly.GetName().Name,
                                module.ApplicationAssemblyName,
                                StringComparison.Ordinal));

                    return applicationAssembly
                        .GetTypes()
                        .Where(IsRequestType)
                        .Select(requestType =>
                        {
                            var relativeNamespaceSegments =
                                GetRelativeNamespace(
                                        requestType,
                                        module.ApplicationAssemblyName)
                                    .Split(
                                        '.',
                                        StringSplitOptions.RemoveEmptyEntries);
                            return new ApplicationUseCase(
                                Module: module,
                                RequestType: requestType,
                                RelativeNamespaceSegments:
                                    relativeNamespaceSegments);
                        });
                })
                .OrderBy(
                    useCase => useCase.RequestType.FullName,
                    StringComparer.Ordinal)
        ];
    }

    private static ApplicationType[] GetApplicationTypes()
    {
        var productionAssemblies = ArchitectureDefinition.ProductionAssemblies
            .ToDictionary(
                assembly => assembly.GetName().Name
                    ?? throw new InvalidOperationException(
                        $"Could not determine assembly name for " +
                        $"'{assembly.FullName}'."),
                StringComparer.Ordinal);

        return
        [
            .. ArchitectureDefinition.Modules
                .SelectMany(module => productionAssemblies[
                        module.ApplicationAssemblyName]
                    .GetTypes()
                    .Where(type => type.Namespace is not null)
                    .Select(type => new ApplicationType(module, type)))
        ];
    }

    private static Type[] GetHandlerRequestTypes(Type type)
    {
        return
        [
            .. type.GetInterfaces()
                .Where(candidate =>
                    candidate.IsGenericType &&
                    HandlerInterfaceDefinitions.Contains(
                        candidate.GetGenericTypeDefinition()))
                .Select(candidate => candidate.GetGenericArguments()[0])
        ];
    }

    private static Type[] GetValidatedTypes(Type type)
    {
        for (var currentType = type;
             currentType is not null;
             currentType = currentType.BaseType)
        {
            if (currentType.IsGenericType &&
                currentType.GetGenericTypeDefinition() ==
                typeof(AbstractValidator<>))
            {
                return [currentType.GetGenericArguments()[0]];
            }
        }

        return [];
    }

    private static bool IsRequestType(Type type)
    {
        return type.GetInterfaces().Any(candidate =>
            candidate.IsGenericType &&
            RequestInterfaceDefinitions.Contains(
                candidate.GetGenericTypeDefinition()));
    }

    private static string GetRequestSuffix(Type requestType)
    {
        return requestType.GetInterfaces().Any(candidate =>
            candidate.IsGenericType &&
            candidate.GetGenericTypeDefinition() == typeof(ICommand<>))
            ? CommandSuffix
            : QuerySuffix;
    }

    private static bool HasNamespaceSegment(
        Type type,
        string namespaceSegment)
    {
        return type.Namespace?
            .Split('.')
            .Contains(namespaceSegment, StringComparer.Ordinal) == true;
    }

    private static bool IsLayerAssembly(
        System.Reflection.Assembly assembly,
        string layerSuffix)
    {
        return assembly.GetName().Name?.EndsWith(
            layerSuffix,
            StringComparison.Ordinal) == true;
    }

    private static string GetExpectedSourceFilePath(
        string repositoryRoot,
        ModuleArchitecture module,
        Type type)
    {
        var assemblyName = type.Assembly.GetName().Name
            ?? throw new InvalidOperationException(
                $"Could not determine assembly for '{type.FullName}'.");
        var moduleDirectoryName =
            module.Name[(module.Name.LastIndexOf('.') + 1)..];
        var relativeNamespace = GetRelativeNamespace(
            type,
            assemblyName);

        return Path.Combine(
            repositoryRoot,
            SourceDirectoryName,
            moduleDirectoryName,
            assemblyName,
            relativeNamespace.Replace(
                '.',
                Path.DirectorySeparatorChar),
            $"{type.Name.Split('`')[0]}.cs");
    }

    private static string[] GetAbstractionSourceFiles(
        string repositoryRoot)
    {
        return
        [
            .. ArchitectureDefinition.Modules
                .SelectMany(module => new[]
                {
                    module.DomainAssemblyName,
                    module.ApplicationAssemblyName
                }
                .SelectMany(assemblyName =>
                {
                    var moduleDirectoryName =
                        module.Name[(module.Name.LastIndexOf('.') + 1)..];
                    var projectDirectory = Path.Combine(
                        repositoryRoot,
                        SourceDirectoryName,
                        moduleDirectoryName,
                        assemblyName);

                    return Directory
                        .EnumerateFiles(
                            projectDirectory,
                            "*.cs",
                            SearchOption.AllDirectories)
                        .Where(path =>
                            HasPathSegment(
                                path,
                                AbstractionsNamespaceSegment) &&
                            !HasPathSegment(path, "bin") &&
                            !HasPathSegment(path, "obj"));
                }))
                .Order(StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static ModuleArchitecture GetModule(Type type)
    {
        var assemblyName = type.Assembly.GetName().Name
            ?? throw new InvalidOperationException(
                $"Could not determine assembly for '{type.FullName}'.");

        return ArchitectureDefinition.Modules.Single(module =>
            string.Equals(
                module.DomainAssemblyName,
                assemblyName,
                StringComparison.Ordinal) ||
            string.Equals(
                module.ApplicationAssemblyName,
                assemblyName,
                StringComparison.Ordinal));
    }

    private static bool HasPathSegment(
        string path,
        string segment)
    {
        return path
            .Split(
                [
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                ],
                StringSplitOptions.RemoveEmptyEntries)
            .Contains(segment, StringComparer.OrdinalIgnoreCase);
    }

    private static string GetRelativeNamespace(
        Type type,
        string assemblyName)
    {
        var typeNamespace = type.Namespace
            ?? throw new InvalidOperationException(
                $"Type '{type.FullName}' does not have a namespace.");
        var namespacePrefix = assemblyName + ".";

        if (!typeNamespace.StartsWith(
                namespacePrefix,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Type '{type.FullName}' namespace must start with " +
                $"'{namespacePrefix}'.");
        }

        return typeNamespace[namespacePrefix.Length..];
    }

    private static string FormatTypes(
        Type[] types)
    {
        return types.Length == 0
            ? "<none>"
            : string.Join(
                ", ",
                types
                    .Select(type => type.FullName)
                    .Order(StringComparer.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(
                    directory.FullName,
                    SourceDirectoryName)))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find repository root containing " +
            $"the '{SourceDirectoryName}' directory.");
    }

    private sealed record ApplicationType(
        ModuleArchitecture Module,
        Type Type);

    private sealed record ApplicationUseCase(
        ModuleArchitecture Module,
        Type RequestType,
        string[] RelativeNamespaceSegments);

}
