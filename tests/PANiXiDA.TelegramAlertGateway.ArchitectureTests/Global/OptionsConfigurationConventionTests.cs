using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Options;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Global;

public sealed class OptionsConfigurationConventionTests
{
    private const string OptionsSuffix = "Options";
    private const string OptionsDirectoryName = "Options";
    private const string SectionNameFieldName = "SectionName";
    private const string SourceDirectoryName = "src";
    private const string ValidateOnStartMethodName = "ValidateOnStart";
    private const string ValidateOptionsTypeName = "IValidateOptions";
    private const string ValidatorSuffix = "Validator";

    private static readonly string[] ServiceRegistrationMethodNames =
    [
        "AddScoped",
        "AddSingleton",
        "AddTransient"
    ];

    [Fact(DisplayName = "Configuration options should have registered validators in same directory when declared")]
    public void ConfigurationOptions_Should_HaveRegisteredValidatorsInSameDirectory_When_Declared()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceFiles = GetProductionSourceFiles(repositoryRoot);
        var optionsTypes = GetConfigurationOptionsTypes();
        var validatorRegistrations = sourceFiles
            .SelectMany(GetValidatorRegistrations)
            .ToArray();
        var violations = optionsTypes
            .SelectMany(optionsType => GetValidatorViolations(
                repositoryRoot: repositoryRoot,
                sourceFiles: sourceFiles,
                validatorRegistrations: validatorRegistrations,
                optionsType: optionsType))
            .ToArray();

        Assert.NotEmpty(optionsTypes);
        Assert.True(
            violations.Length == 0,
            $"Configuration options must have a registered " +
            $"IValidateOptions<TOptions> validator in the same directory:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Configuration options should use ValidateOnStart when registered")]
    public void ConfigurationOptions_Should_UseValidateOnStart_When_Registered()
    {
        var repositoryRoot = FindRepositoryRoot();
        var optionsTypes = GetConfigurationOptionsTypes();
        var registrations = GetProductionSourceFiles(repositoryRoot)
            .SelectMany(sourceFile => GetOptionsRegistrations(
                repositoryRoot,
                sourceFile))
            .ToArray();
        var violations = optionsTypes
            .SelectMany(optionsType => GetValidateOnStartViolations(
                optionsType,
                registrations))
            .ToArray();

        Assert.NotEmpty(optionsTypes);
        Assert.True(
            violations.Length == 0,
            $"Configuration options must be registered through " +
            $"AddOptions<TOptions>() with ValidateOnStart():" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Configuration options should reside in dedicated Options subdirectories when declared")]
    public void ConfigurationOptions_Should_ResideInDedicatedOptionsSubdirectories_When_Declared()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceFiles = GetProductionSourceFiles(repositoryRoot);
        var optionsTypes = GetConfigurationOptionsTypes();
        var violations = optionsTypes
            .SelectMany(optionsType => GetOptionsDirectoryViolations(
                repositoryRoot,
                sourceFiles,
                optionsType))
            .ToArray();

        Assert.NotEmpty(optionsTypes);
        Assert.True(
            violations.Length == 0,
            $"Configuration options must reside in dedicated " +
            $"'{OptionsDirectoryName}/<Name>/' subdirectories:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static Type[] GetConfigurationOptionsTypes()
    {
        return
        [
            .. ArchitectureDefinition.ProductionAssemblies
                .SelectMany(assembly => assembly.GetTypes())
                .Where(IsConfigurationOptionsType)
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
        ];
    }

    private static bool IsConfigurationOptionsType(Type type)
    {
        var sectionNameField = type.GetField(
            SectionNameFieldName,
            BindingFlags.DeclaredOnly |
            BindingFlags.Public |
            BindingFlags.Static);

        return type is { IsClass: true, IsAbstract: false } &&
               type.Name.EndsWith(OptionsSuffix, StringComparison.Ordinal) &&
               sectionNameField is { IsLiteral: true } &&
               sectionNameField.FieldType == typeof(string);
    }

    private static IEnumerable<string> GetValidatorViolations(
        string repositoryRoot,
        IReadOnlyCollection<string> sourceFiles,
        IReadOnlyCollection<ValidatorRegistration> validatorRegistrations,
        Type optionsType)
    {
        var validatorTypeName = optionsType.Name + ValidatorSuffix;
        var validatorFullName = $"{optionsType.Namespace}.{validatorTypeName}";
        var validatorType = optionsType.Assembly.GetType(
            name: validatorFullName,
            throwOnError: false,
            ignoreCase: false);

        if (validatorType is null)
        {
            yield return $"{optionsType.FullName} requires " +
                         $"{validatorFullName}.";
            yield break;
        }

        var validateOptionsType = typeof(IValidateOptions<>)
            .MakeGenericType(optionsType);

        if (!validateOptionsType.IsAssignableFrom(validatorType))
        {
            yield return $"{validatorType.FullName} must implement " +
                         $"IValidateOptions<{optionsType.Name}>.";
        }

        if (!validatorRegistrations.Any(registration =>
                string.Equals(
                    registration.OptionsTypeName,
                    optionsType.Name,
                    StringComparison.Ordinal) &&
                string.Equals(
                    registration.ValidatorTypeName,
                    validatorType.Name,
                    StringComparison.Ordinal)))
        {
            yield return $"{validatorType.FullName} must be registered for " +
                         $"IValidateOptions<{optionsType.Name}>.";
        }

        var optionsSourceFiles = FindSourceFiles(
            sourceFiles,
            optionsType);
        var validatorSourceFiles = FindSourceFiles(
            sourceFiles,
            validatorType);

        if (optionsSourceFiles.Length != 1)
        {
            yield return $"{optionsType.FullName} must have exactly one " +
                         $"source declaration; found " +
                         $"{optionsSourceFiles.Length}.";
        }

        if (validatorSourceFiles.Length != 1)
        {
            yield return $"{validatorType.FullName} must have exactly one " +
                         $"source declaration; found " +
                         $"{validatorSourceFiles.Length}.";
        }

        if (optionsSourceFiles.Length == 1 &&
            validatorSourceFiles.Length == 1 &&
            !string.Equals(
                Path.GetDirectoryName(optionsSourceFiles[0]),
                Path.GetDirectoryName(validatorSourceFiles[0]),
                StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{optionsType.FullName} and " +
                         $"{validatorType.FullName} must reside in the same " +
                         $"directory; found " +
                         $"'{Path.GetRelativePath(
                             repositoryRoot,
                             optionsSourceFiles[0])}' and " +
                         $"'{Path.GetRelativePath(
                             repositoryRoot,
                             validatorSourceFiles[0])}'.";
        }
    }

    private static IEnumerable<string> GetValidateOnStartViolations(
        Type optionsType,
        IReadOnlyCollection<OptionsRegistration> registrations)
    {
        var optionsRegistrations = registrations
            .Where(registration => string.Equals(
                registration.OptionsTypeName,
                optionsType.Name,
                StringComparison.Ordinal))
            .ToArray();

        if (optionsRegistrations.Length == 0)
        {
            yield return $"{optionsType.FullName} is not registered through " +
                         $"AddOptions<{optionsType.Name}>().";
            yield break;
        }

        foreach (var registration in optionsRegistrations
                     .Where(registration => !registration.UsesValidateOnStart))
        {
            yield return $"{optionsType.FullName} registration at " +
                         $"'{registration.SourceLocation}' must call " +
                         $"{ValidateOnStartMethodName}().";
        }
    }

    private static IEnumerable<string> GetOptionsDirectoryViolations(
        string repositoryRoot,
        IReadOnlyCollection<string> sourceFiles,
        Type optionsType)
    {
        var optionsSourceFiles = FindSourceFiles(
            sourceFiles,
            optionsType);

        if (optionsSourceFiles.Length != 1)
        {
            yield return $"{optionsType.FullName} must have exactly one " +
                         $"source declaration; found " +
                         $"{optionsSourceFiles.Length}.";
            yield break;
        }

        var optionsDirectoryPath = Path.GetDirectoryName(
            optionsSourceFiles[0]);

        if (optionsDirectoryPath is null)
        {
            yield return $"{optionsType.FullName} source directory could " +
                         $"not be determined.";
            yield break;
        }

        var optionsDirectory = new DirectoryInfo(optionsDirectoryPath);
        var expectedDirectoryName = optionsType.Name[..^OptionsSuffix.Length];

        if (!string.Equals(
                optionsDirectory.Name,
                expectedDirectoryName,
                StringComparison.Ordinal) ||
            !string.Equals(
                optionsDirectory.Parent?.Name,
                OptionsDirectoryName,
                StringComparison.Ordinal))
        {
            yield return $"{optionsType.FullName} must reside under " +
                         $"'{OptionsDirectoryName}/" +
                         $"{expectedDirectoryName}/'; found " +
                         $"'{Path.GetRelativePath(
                             repositoryRoot,
                             optionsSourceFiles[0])}'.";
        }
    }

    private static IEnumerable<ValidatorRegistration>
        GetValidatorRegistrations(string sourceFile)
    {
        var syntaxRoot = CSharpSyntaxTree
            .ParseText(File.ReadAllText(sourceFile))
            .GetRoot();

        foreach (var invocation in syntaxRoot
                     .DescendantNodes()
                     .OfType<InvocationExpressionSyntax>())
        {
            var genericName = GetInvokedGenericName(invocation);

            if (genericName is null ||
                !ServiceRegistrationMethodNames.Contains(
                    genericName.Identifier.ValueText,
                    StringComparer.Ordinal))
            {
                continue;
            }

            var optionsTypeName = genericName.TypeArgumentList.Arguments
                .Select(GetValidateOptionsTargetName)
                .FirstOrDefault(name => name is not null);
            var validatorTypeName = genericName.TypeArgumentList.Arguments
                .Select(GetSimpleTypeName)
                .FirstOrDefault(name =>
                    name.EndsWith(
                        OptionsSuffix + ValidatorSuffix,
                        StringComparison.Ordinal));

            if (optionsTypeName is not null &&
                validatorTypeName is not null)
            {
                yield return new ValidatorRegistration(
                    optionsTypeName,
                    validatorTypeName);
            }
        }
    }

    private static IEnumerable<OptionsRegistration> GetOptionsRegistrations(
        string repositoryRoot,
        string sourceFile)
    {
        var syntaxRoot = CSharpSyntaxTree
            .ParseText(File.ReadAllText(sourceFile))
            .GetRoot();

        return syntaxRoot
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => new
            {
                Invocation = invocation,
                GenericName = GetInvokedGenericName(invocation)
            })
            .Where(candidate =>
                candidate.GenericName is not null &&
                string.Equals(
                    candidate.GenericName.Identifier.ValueText,
                    "AddOptions",
                    StringComparison.Ordinal) &&
                candidate.GenericName.TypeArgumentList.Arguments.Count == 1)
            .Select(candidate => new OptionsRegistration(
                OptionsTypeName: GetSimpleTypeName(
                    candidate.GenericName!.TypeArgumentList.Arguments[0]),
                SourceLocation:
                    $"{Path.GetRelativePath(repositoryRoot, sourceFile)}:" +
                    $"{candidate.Invocation.GetLocation()
                        .GetLineSpan()
                        .StartLinePosition.Line + 1}",
                UsesValidateOnStart: HasChainedInvocation(
                    candidate.Invocation,
                    ValidateOnStartMethodName)));
    }

    private static bool HasChainedInvocation(
        InvocationExpressionSyntax invocation,
        string methodName)
    {
        SyntaxNode currentNode = invocation;

        while (currentNode.Parent is MemberAccessExpressionSyntax
            {
                Parent: InvocationExpressionSyntax chainedInvocation
            })
        {
            if (string.Equals(
                    GetInvokedMethodName(chainedInvocation),
                    methodName,
                    StringComparison.Ordinal))
            {
                return true;
            }

            currentNode = chainedInvocation;
        }

        return false;
    }

    private static GenericNameSyntax? GetInvokedGenericName(
        InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax
            {
                Name: GenericNameSyntax genericName
            } => genericName,
            GenericNameSyntax genericName => genericName,
            _ => null
        };
    }

    private static string? GetInvokedMethodName(
        InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess =>
                memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier =>
                identifier.Identifier.ValueText,
            GenericNameSyntax genericName =>
                genericName.Identifier.ValueText,
            _ => null
        };
    }

    private static string? GetValidateOptionsTargetName(
        TypeSyntax typeSyntax)
    {
        var validateOptionsType = typeSyntax
            .DescendantNodesAndSelf()
            .OfType<GenericNameSyntax>()
            .SingleOrDefault(genericName =>
                string.Equals(
                    genericName.Identifier.ValueText,
                    ValidateOptionsTypeName,
                    StringComparison.Ordinal));

        return validateOptionsType?.TypeArgumentList.Arguments.Count == 1
            ? GetSimpleTypeName(
                validateOptionsType.TypeArgumentList.Arguments[0])
            : null;
    }

    private static string GetSimpleTypeName(TypeSyntax typeSyntax)
    {
        return typeSyntax switch
        {
            IdentifierNameSyntax identifier =>
                identifier.Identifier.ValueText,
            GenericNameSyntax genericName =>
                genericName.Identifier.ValueText,
            QualifiedNameSyntax qualifiedName =>
                GetSimpleTypeName(qualifiedName.Right),
            AliasQualifiedNameSyntax aliasQualifiedName =>
                GetSimpleTypeName(aliasQualifiedName.Name),
            NullableTypeSyntax nullableType =>
                GetSimpleTypeName(nullableType.ElementType),
            _ => typeSyntax.ToString()
        };
    }

    private static string[] FindSourceFiles(
        IReadOnlyCollection<string> sourceFiles,
        Type type)
    {
        return
        [
            .. sourceFiles
                .Where(sourceFile => string.Equals(
                    Path.GetFileNameWithoutExtension(sourceFile),
                    type.Name,
                    StringComparison.Ordinal))
                .Where(sourceFile => DeclaresType(sourceFile, type))
                .Order(StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static bool DeclaresType(string sourceFile, Type type)
    {
        var syntaxRoot = CSharpSyntaxTree
            .ParseText(File.ReadAllText(sourceFile))
            .GetRoot();

        return syntaxRoot
            .DescendantNodes()
            .OfType<BaseTypeDeclarationSyntax>()
            .Any(declaration =>
                string.Equals(
                    declaration.Identifier.ValueText,
                    type.Name,
                    StringComparison.Ordinal) &&
                string.Equals(
                    GetNamespace(declaration),
                    type.Namespace,
                    StringComparison.Ordinal));
    }

    private static string GetNamespace(
        BaseTypeDeclarationSyntax declaration)
    {
        return string.Join(
            ".",
            declaration
                .Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(namespaceDeclaration =>
                    namespaceDeclaration.Name.ToString()));
    }

    private static string[] GetProductionSourceFiles(string repositoryRoot)
    {
        return
        [
            .. Directory
                .EnumerateFiles(
                    path: Path.Combine(
                        repositoryRoot,
                        SourceDirectoryName),
                    searchPattern: "*.cs",
                    searchOption: SearchOption.AllDirectories)
                .Where(sourceFile =>
                    !HasPathSegment(sourceFile, "bin") &&
                    !HasPathSegment(sourceFile, "obj") &&
                    !HasPathSegment(sourceFile, "Generated") &&
                    !HasPathSegment(sourceFile, "Migrations"))
                .Order(StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static bool HasPathSegment(string path, string segment)
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

    private sealed record OptionsRegistration(
        string OptionsTypeName,
        string SourceLocation,
        bool UsesValidateOnStart);

    private sealed record ValidatorRegistration(
        string OptionsTypeName,
        string ValidatorTypeName);
}
