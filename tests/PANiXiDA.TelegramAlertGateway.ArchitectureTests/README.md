# Архитектурные тесты

Проект содержит автоматические ограничения на зависимости между слоями и
модулями, устройство доменной модели, соглашения Application, Infrastructure и
Presentation, а также единый формат тестов.

Запуск из корня репозитория:

```powershell
dotnet test tests/PANiXiDA.TelegramAlertGateway.ArchitectureTests/PANiXiDA.TelegramAlertGateway.ArchitectureTests.csproj
```

## Границы слоёв и модулей

1. `DomainLayer_Should_NotDependOnOuterLayers_When_Validated` — типы слоя
   `Domain` не должны зависеть от `Contracts`, `Application`, `Infrastructure`,
   `Presentation` или `Host`. Разрешены только сам Domain и общие внешние
   доменные абстракции.

2. `ApplicationLayer_Should_DependOnlyOnDomainAndSharedAbstractions_When_Validated`
   — типы слоя `Application` не должны зависеть от `Infrastructure`,
   `Presentation` или `Host`. Они могут использовать Domain, Contracts и общие
   прикладные абстракции.

3. `InfrastructureLayer_Should_NotDependOnPresentationOrHost_When_Validated` —
   типы слоя `Infrastructure` не должны зависеть от `Presentation` или `Host`.

4. `PresentationLayer_Should_NotDependOnDomainInfrastructureOrHost_When_Validated`
   — типы слоя `Presentation` не должны напрямую зависеть от `Domain`,
   `Infrastructure` или `Host`. Взаимодействие с бизнес-логикой выполняется
   через Application.

5. `ContractsLayer_Should_NotDependOnModuleLayersOrHost_When_Validated` — типы
   `Contracts` не должны зависеть от `Domain`, `Application`, `Infrastructure`,
   `Presentation` или `Host`. Контракты остаются независимой границей модуля.

6. `Modules_Should_HaveAllExpectedLayerAssemblies_When_Discovered` — каждый
   обнаруженный модуль должен содержать пять сборок с одинаковым префиксом:
   `.Contracts`, `.Domain`, `.Application`, `.Infrastructure` и
   `.Presentation`.

7. `ModuleLayers_Should_NotDependOnOtherModuleInternals_When_Validated` — типы
   `Domain`, `Application`, `Infrastructure` и `Presentation` одного модуля не
   должны зависеть от внутренних слоёв другого модуля. Межмодульные зависимости
   разрешены только через сборки `.Contracts`.

8. `ModuleProjectReferences_Should_FollowAllowedDependencies_When_Validated` —
   прямые `ProjectReference` модулей должны соответствовать следующему графу:
   `Contracts` и `Domain` не ссылаются на внутренние проекты; `Application`
   ссылается только на свой `Domain` и при необходимости на Contracts;
   `Infrastructure` — на свои `Domain` и `Application`, а также на Contracts;
   `Presentation` — на свой `Application` и Contracts. Ссылка на Contracts
   может вести как в свой, так и в другой модуль.

`Modules_Should_HaveHostConfigurationsInModulesDirectory_When_Discovered` —
каждый обнаруженный модуль должен иметь `<Module>ModuleConfiguration` в
`Host/Configurations/Modules`.

## Domain

В папках и пространствах имён `Abstractions` слоя Domain располагаются только
абстракции. Интерфейс репозитория конкретного агрегата, наследующий
`IRepository<,>`, является доменным контрактом и должен находиться в
`Domain/<Aggregates>/Abstractions`. Generic-параметрами являются strongly typed
ID и aggregate root. Методы такого репозитория работают только с aggregate root,
value object, `Enumeration<>` и strongly typed ID; примитивы и прочие типы в
предметных параметрах и результатах запрещены. `Task`, `ValueTask`, коллекции,
nullable-обёртки и `CancellationToken` считаются техническими типами.

9. `AggregateRoots_Should_ContainOnlyDomainTypes_When_StateIsDeclared` — поля
   aggregate root могут содержать только value object, strongly typed ID,
   `Enumeration<>` или entity. Для коллекций проверяется тип элемента.

10. `Entities_Should_ContainOnlyDomainTypes_When_StateIsDeclared` — поля entity,
    не являющихся aggregate root, могут содержать только value object, strongly
    typed ID или `Enumeration<>`. Вложенные entity для них запрещены.

11. `DomainTypes_Should_HaveMatchingUnitTestFiles_When_DomainTypesAreDeclared` —
    каждый aggregate root, entity, value object, strongly typed ID и
    `Enumeration<>` должен иметь отдельный файл unit-тестов. Путь файла повторяет
    модуль, относительный namespace и имя доменного типа.

12. `DomainUnitTests_Should_CoverEveryAccessibleMethod_When_DomainMethodsAreDeclared`
    — для каждого публичного, internal или protected internal доменного метода
    должен существовать тестовый метод с префиксом
    `<ИмяМетода>_Should_`. Для перегрузок требуется соответствующее количество
    тестовых методов.

13. `AggregateRootsAndEntities_Should_NotDeclarePublicSetters_When_DomainStateIsDeclared`
    — свойства aggregate root и entity не должны иметь публичные setter.
    Изменение состояния разрешено только через контролируемое поведение
    доменной модели.

14. `AggregateRootsAndEntities_Should_NotExposeMutableCollections_When_PublicStateIsDeclared`
    — публичные поля, свойства и возвращаемые значения методов aggregate root и
    entity не должны раскрывать массивы, `ICollection<>`, `IList<>`,
    `IDictionary<,>`, `ISet<>` или их реализации. В том числе проверяются
    изменяемые коллекции, вложенные в generic-обёртки.

15. `AggregateRoots_Should_NotContainOtherAggregateRoots_When_StateIsDeclared` —
    поля aggregate root не должны содержать другой aggregate root напрямую,
    через массив или через generic-коллекцию.

16. `ConcreteDomainClasses_Should_BeSealed_When_Declared` — каждый конкретный
    класс в сборках `.Domain`, включая aggregate root, entity, value object,
    enumeration и domain event, должен быть `sealed`.

17. `AggregateRoots_Should_HaveSingularNamesAndPluralDirectories_When_Declared`
    — имя типа aggregate root должно быть в единственном числе, а его namespace
    и физическая папка — во множественном. Например, aggregate root `User`
    размещается в `Users/User.cs`, а `Faction` — в `Factions/Faction.cs`.

18. `Entities_Should_HaveSingularNamesAndPluralDirectories_When_Declared` — имя
    типа entity должно быть в единственном числе. Entity размещается под своим
    aggregate root или родительской entity по пути
    `Entities/<EntityName во множественном числе>/<EntityName>.cs`. Например,
    `UserClaim` размещается в `Users/Entities/UserClaims/UserClaim.cs`.

19. `ValueObjects_Should_ResideInOwnerValueObjectsDirectories_When_Declared` —
    каждый value object должен находиться в папке и namespace `ValueObjects`
    непосредственно под владеющим aggregate root или entity. Например,
    `UserName` находится в `Users/ValueObjects`, а `ClaimType` для `UserClaim` —
    в `Users/Entities/UserClaims/ValueObjects`.

20. `Enumerations_Should_ResideInOwnerEnumerationsDirectories_When_Declared` —
    каждый `Enumeration<>` должен находиться в папке и namespace `Enumerations`
    непосредственно под владеющим aggregate root или entity. Например,
    `UserStatus` находится в `Users/Enumerations`.

21. `DomainEvents_Should_ResideInEventsDirectories_When_Declared` — каждый
    domain event должен находиться в папке и namespace `Events` своей доменной
    области или владельца. Например, события `User` находятся в
    `Users/Events`.

22. `StronglyTypedIds_Should_MatchOwnerNamesAndLocations_When_Declared` —
    strongly typed ID каждого aggregate root или entity называется
    `<OwnerName>Id`, реализует `IStronglyTypedId` и лежит в той же папке и
    namespace, что и владелец. Например, `UserId` лежит рядом с `User`, а
    `UserClaimId` — рядом с `UserClaim`. Неиспользуемые strongly typed ID
    запрещены.

23. `Repositories_Should_ResideInDomainAbstractions_When_Declared` — интерфейс,
    наследующий `IRepository<,>`, должен находиться в Domain в папке и
    namespace `Abstractions`.

24. `Repositories_Should_UseStronglyTypedIdsAndAggregateRoots_When_Declared` —
    первый generic-параметр `IRepository<,>` должен быть непримитивным strongly
    typed ID, а второй — aggregate root.

25. `RepositoryMethods_Should_UseOnlyDomainTypes_When_Declared` — параметры и
    результаты методов `IRepository<,>` могут содержать только aggregate root,
    value object, `Enumeration<>` и strongly typed ID. Разрешены технические
    обёртки `Task`, `ValueTask`, коллекции, nullable и `CancellationToken`;
    примитивы, entity, domain events и произвольные DTO запрещены.

26. `Repositories_Should_MatchPluralAggregateNames_When_Declared` — интерфейс,
    наследующий `IRepository<,>`, должен называться
    `I<AggregatePlural>Repository` и находиться в одноимённой feature-папке.
    Корректное английское множественное число строится через Humanizer.

27. `ConstructorParameters_Should_FollowTypeBasedNaming_When_RepositoryIsInjected`
    — параметр конструктора типа repository именуется по типу интерфейса без
    начальной `I` и с маленькой первой буквы: `IFactionsRepository`
    превращается в `factionsRepository`.

## Application

Read-side не использует типы из Domain. Generic-идентификатор
`IReadRepository<>` является примитивом. Дополнительные параметры его методов
могут быть примитивами, коллекциями примитивов или Application-моделями
параметров, рекурсивно составленными только из таких значений. Результаты read
repository и query handler реализуют `IReadModel`; коллекции,
`Result`, `Task` и модели пагинации могут использоваться как обёртки.

28. `ApplicationUseCases_Should_ResideInFeatureFolders_When_Declared` — каждый
    `ICommand` и `IQuery` должен находиться в папке конкретной фичи ниже хотя бы
    одной группирующей папки. Между корнем Application и feature-папкой
    разрешено любое количество логических подпапок, например
    `Auth/ChangePassword` или `Users/Administration/Block`.

29. `ApplicationUseCaseTypes_Should_HaveExpectedRoleSuffixes_When_Declared` —
    `ICommand` оканчивается на `Command`, `IQuery` — на `Query`,
    `ICommandHandler<,>` и `IQueryHandler<,>` — на `Handler`, validator — на
    `Validator`.

30. `ApplicationUseCaseParts_Should_ShareOneFeatureFolder_When_Declared` —
    command или query должен иметь ровно один handler и один validator. Request,
    handler и validator именуются согласованно и располагаются в одной общей
    feature-папке и namespace.

31. `AbstractionsNamespaces_Should_ContainOnlyAbstractions_When_Declared` —
    папки и пространства имён `Abstractions` в Domain и Application могут
    содержать только интерфейсы, абстрактные типы и делегаты.

32. `ReadRepositories_Should_ResideInApplicationAbstractions_When_Declared` —
    интерфейс, наследующий `IReadRepository<>`, должен находиться в Application
    в папке и namespace `Abstractions`.

33. `ReadRepositories_Should_UsePrimitiveIds_When_Declared` —
    generic-идентификатор `IReadRepository<>` должен быть примитивом, например
    `Guid`; strongly typed ID и другие доменные идентификаторы запрещены.

34. `ReadRepositoryMethods_Should_UseOnlyPrimitiveInputModels_When_Declared` —
    параметры дополнительных методов read repository могут быть примитивами,
    `CancellationToken`, коллекциями примитивов или Application-моделями,
    публичное состояние которых рекурсивно состоит только из разрешённых типов.
    Любые типы из Domain и `IReadModel` во входных параметрах запрещены.

35. `ReadRepositoryMethods_Should_ReturnReadModels_When_Declared` — каждый
    дополнительный метод read repository должен возвращать реализацию
    `IReadModel`. Допускаются обёртки `Task`, `Result`, коллекции и модели
    пагинации. Базовые методы `ExistsByIdAsync` и `AnyAsync`, возвращающие
    `bool`, к этому правилу не относятся.

36. `ReadModels_Should_EndWithReadModel_When_Declared` — каждый конкретный
    тип, реализующий `IReadModel`, должен оканчиваться на `ReadModel`.

37. `TypesEndingWithReadModel_Should_ImplementIReadModel_When_Declared` — каждый
    конкретный Application-тип с суффиксом `ReadModel` должен реализовывать
    `IReadModel`.

38. `QueryHandlers_Should_ReturnReadModels_When_Declared` — payload результата
    каждого `IQueryHandler<,>` должен реализовывать `IReadModel`. Допускаются
    обёртки `Task`, `Result`, коллекции и модели пагинации.

39. `ReadRepositories_Should_MatchPluralAggregateNames_When_Declared` — интерфейс,
    наследующий `IReadRepository<>`, должен называться
    `I<AggregatePlural>ReadRepository` и находиться в одноимённой
    feature-папке. Корректное английское множественное число строится через
    Humanizer.

40. `ConstructorParameters_Should_FollowTypeBasedNaming_When_ReadRepositoryIsInjected`
    — параметр конструктора типа read repository строится по тому же правилу:
    `IFactionsReadRepository` превращается в `factionsReadRepository`.

41. `ApplicationHandlers_Should_HaveMatchingUnitTestFiles_When_HandlersAreDeclared`
    — каждый конкретный `ICommandHandler<,>`, `IQueryHandler<,>` или
    `IEventHandler<>` в Application должен иметь отдельный файл unit-тестов.
    Путь файла повторяет модуль, относительный namespace и имя handler.

42. `ApplicationHandlerUnitTests_Should_CoverEveryHandlerMethod_When_HandlersAreDeclared`
    — для каждого метода реализуемого handler-контракта должен существовать
    тестовый метод с префиксом `<ИмяМетода>_Should_`. Для перегрузок учитывается
    количество методов с одинаковым именем.

43. `CommandAndQueryHandlers_Should_HaveValidators_When_HandlersAreDeclared` —
    request каждого command или query handler должен иметь реализацию
    `IValidator<TRequest>`. Для event handler validator не требуется.

44. `ApplicationValidators_Should_HaveMatchingUnitTestFiles_When_ValidatorsAreDeclared`
    — каждая конкретная реализация `IValidator<T>` в Application должна иметь
    отдельный непустой файл unit-тестов по пути, соответствующему её модулю,
    namespace и имени.

45. `ApplicationHandlers_Should_BeSealed_When_Declared` — каждый конкретный
    command, query или event handler в сборках `.Application` должен быть
    `sealed`.

## Infrastructure

46. `RepositoryInterfaces_Should_HaveExactlyOneImplementation_When_Declared` —
    каждый интерфейс, наследующий `IRepository<,>` или `IReadRepository<>`,
    должен иметь ровно одну конкретную реализацию в Infrastructure.

47. `AggregateRoots_Should_HaveRegisteredRepositories_When_Declared` — каждый
    aggregate root должен иметь ровно один repository в корне своей
    `Persistence/Features/<AggregatePlural>/Write` feature и этот repository
    должен быть зарегистрирован в DI модуля.

48. `AggregateRoots_Should_HavePersistenceConfigurations_When_Declared` —
    каждый aggregate root должен иметь отдельную EF Core configuration в корне
    Write feature либо явную inline-конфигурацию соответствующей Identity
    persistence-модели. Итоговая EF-модель write DbContext должна содержать
    настроенный тип.

49. `RepositoryImplementations_Should_UsePluralAggregateNames_When_Declared` —
    каждый наследник `IRepository<,>` должен иметь ровно одну реализацию с
    именем `<AggregatePlural>Repository`, например `FactionsRepository`.

50. `ReadRepositoryImplementations_Should_UsePluralAggregateNames_When_Declared`
    — каждый наследник `IReadRepository<>` должен иметь ровно одну реализацию с
    именем `<AggregatePlural>ReadRepository`, например
    `FactionsReadRepository`.

51. `RepositoryImplementations_Should_ResideInWriteRoots_When_Declared` —
    реализация `IRepository<,>` должна находиться непосредственно в
    `Persistence/Features/<AggregatePlural>/Write`.

52. `ReadRepositoryImplementations_Should_ResideInReadRoots_When_Declared` —
    реализация `IReadRepository<>` должна находиться непосредственно в
    `Persistence/Features/<AggregatePlural>/Read`.

53. `ReadModelMappers_Should_EndWithReadModelMapper_When_Declared` — каждая
    реализация `IReadModelMapper<,,>` должна оканчиваться на
    `ReadModelMapper`.

54. `ReadModelMappers_Should_ResideInAggregateReadMappersDirectories_When_Declared`
    — реализации `IReadModelMapper<,,>` должны находиться в
    `Persistence/Features/<AggregatePlural>/Read/Mappers`.

55. `ReadDatabaseModels_Should_EndWithReadDbModel_When_Declared` — каждый
    наследник `ReadDbModel<>` или `AuditableReadDbModel<>` должен оканчиваться
    на `ReadDbModel`.

56. `ReadDatabaseModels_Should_ResideInAggregateReadDbModelsDirectories_When_Declared`
    — read database models должны находиться в
    `Persistence/Features/<AggregatePlural>/Read/DbModels`.

57. `AuditableEntityConfigurations_Should_ResideInAggregateWriteRoots_When_Declared`
    — наследники `AuditableEntityConfiguration<>` должны находиться
    непосредственно в `Persistence/Features/<AggregatePlural>/Write`.

58. `EntityTypeConfigurations_Should_ResideInAggregateWriteRoots_When_Declared`
    — реализации `IEntityTypeConfiguration<>` должны находиться непосредственно
    в `Persistence/Features/<AggregatePlural>/Write`.

59. `AuditableEntityConfigurations_Should_AvoidExplicitStoreObjectNames_When_Declared`
    — наследники `AuditableEntityConfiguration<>` не должны явно задавать имена
    таблиц, представлений и столбцов через `ToTable`, `ToView` или
    `HasColumnName`.

60. `EntityTypeConfigurations_Should_AvoidExplicitStoreObjectNames_When_Declared`
    — реализации `IEntityTypeConfiguration<>` подчиняются тому же запрету на
    явные имена таблиц, представлений и столбцов.

61. `ReadDatabaseContexts_Should_MatchModuleNamesAndResideInPersistenceCore_When_Declared`
    — наследник `ReadDbContext<>` называется `<Module>ReadDbContext` и находится
    непосредственно в `Persistence/Core`.

62. `WriteDatabaseContexts_Should_MatchModuleNamesAndResideInPersistenceCore_When_Declared`
    — наследник `WriteDbContext<>` называется `<Module>WriteDbContext` и
    находится непосредственно в `Persistence/Core`.

63. `MigrationsAndModelSnapshots_Should_ResideInPersistenceCoreMigrations_When_Declared`
    — EF Core migrations и model snapshots должны находиться в
    `Persistence/Core/Migrations`.

64. `RepositoryImplementations_Should_BeSealed_When_Declared` — каждый
    конкретный класс Infrastructure, реализующий `IRepository<,>` или
    `IReadRepository<>`, должен быть `sealed`.

65. `InfrastructureImplementations_Should_HaveMatchingIntegrationTestFiles_When_ApplicationInterfacesAreImplemented`
    — каждый конкретный класс Infrastructure, реализующий интерфейс из
    Application своего модуля, должен иметь отдельный integration-test файл.
    Путь повторяет относительный namespace и имя реализации.

66. `IntegrationTests_Should_CoverEveryApplicationInterfaceMethod_When_ImplementationExists`
    — для каждого метода реализуемого Application-интерфейса должен существовать
    integration-тест с префиксом `<ИмяМетода>_Should_`. Для перегрузок
    учитывается количество методов с одинаковым именем.

## Presentation

67. `EndpointGroups_Should_ResideInFeatureRootsAndMatchFeatureNames_When_Declared`
    — каждый конкретный `IEndpointGroup` должен находиться непосредственно в
    `Features/<AggregatePlural>`, называться `<AggregatePlural>Endpoints`, а его
    `Name` должен совпадать с `<AggregatePlural>`.

68. `EndpointGroupMetadataProperties_Should_BeGetOnly_When_GroupIsDeclared` —
    свойства `Route`, `Name` и `ApiVersion` каждого `IEndpointGroup` должны
    предоставлять только getter.

69. `Endpoints_Should_ResideInFeatureSlicesUnderTheirGenericGroups_When_Declared`
    — каждый конкретный `IEndpoint<TGroup>` должен находиться в feature-папке
    внутри дерева своего `TGroup`; между корнем группы и feature-папкой
    допускаются логические подпапки. Generic-параметр обязан указывать на
    `IEndpointGroup` из корня этого дерева.

70. `Endpoints_Should_EndWithEndpoint_When_Declared` — каждый конкретный
    `IEndpoint` должен оканчиваться на `Endpoint`.

71. `MapperlyMappers_Should_EndWithMapper_When_Declared` — каждый mapper,
    объявленный через Mapperly, должен оканчиваться на `Mapper`.

72. `EndpointInputTypes_Should_EndWithRequest_When_Declared` — входной
    Presentation-контракт endpoint должен оканчиваться на `Request`.

73. `EndpointOutputTypes_Should_EndWithResponse_When_Declared` — выходной
    Presentation-контракт endpoint должен оканчиваться на `Response`.

74. `EndpointSliceParts_Should_ShareOneFeatureFolder_When_Declared` —
    `Endpoint`, его `Request`, `Response` и используемые `Mapper` должны
    находиться в одной feature-папке и одном namespace.

75. `CreatedAtRouteCalls_Should_UseEndpointNames_When_Declared` — каждый
    `CreatedAtRoute` должен передавать `routeName` строготипизированно через
    `new <Target>Endpoint().Name`.

76. `MediatorMessages_Should_BeCreatedBySliceMappers_When_EndpointSendsAMessage`
    — endpoint должен обращаться к Application через `IMediator`, а первым
    аргументом фактического `IMediator.SendAsync` или `IMediator.QueryAsync`
    должен быть непосредственный вызов mapper своего slice. Проверка не зависит
    от имени переменной mediator.

77. `Endpoints_Should_HaveMatchingFunctionalTestFiles_When_Declared` — каждый
    конкретный `IEndpoint` должен иметь functional-test файл в том же модуле.
    Путь повторяет относительный namespace и имя endpoint.

78. `EndpointMetadata_Should_FollowNamingConventions_When_EndpointIsDeclared` —
    `Route` endpoint и endpoint group состоит из английских lowercase
    kebab-case сегментов и параметров вида `{name}` или `{name:constraint}`;
    `Name` является одним английским PascalCase-идентификатором; `Summary`
    endpoint записывается на английском в sentence case с одиночными пробелами.

79. `EndpointsAndGroups_Should_BeSealed_When_Declared` — каждый конкретный
    `IEndpoint` и `IEndpointGroup` в сборках `.Presentation` должен быть
    `sealed`.

## Глобальные соглашения

80. `Namespaces_Should_MatchFolderStructure_When_Declared` — namespace каждого
    объявленного типа в C#-исходниках проектов из `src`, `tests` и `tools`
    должен в точности совпадать с корневым namespace проекта, дополненным
    относительным путём к папке файла. Для файла в корне проекта используется
    только корневой namespace. Исходники из `bin`, `obj` и `Generated` не
    проверяются.

81. `InvocationAndConstructorArguments_Should_BeNamed_When_Ambiguous` —
    аргументы `null`, `default`, `true` и `false`, а также все аргументы вызова
    с тремя и более аргументами в авторских C#-исходниках из `src` должны
    передаваться по имени параметра. Вызовы методов `System.String`, вызовы с
    `params`, `nameof`, EF migrations, `bin`, `obj` и `Generated` не проверяются.

82. `AsynchronousMethods_Should_EndWithAsync_When_Declared` — каждый
    production-метод, возвращающий `Task`, `ValueTask`, async stream или
    объявленный через `async`, должен оканчиваться на `Async`. Реализации
    внешних интерфейсов и overrides не проверяются, поскольку их имена задаются
    внешним контрактом.

83. `SynchronousMethods_Should_NotEndWithAsync_When_Declared` — синхронные
    production-методы не должны оканчиваться на `Async`.

84. `CurrentTimeAccess_Should_UseUtcSources_When_Declared` — текущее время в
    авторских C#-исходниках из `src` должно получаться через
    `TimeProvider.GetUtcNow()`. В явных конструкторах также разрешены
    `DateTime.UtcNow` и `DateTimeOffset.UtcNow` как часть жизненного цикла
    создаваемого объекта. `DateTime.Now`, `DateTime.Today`,
    `DateTimeOffset.Now` и `TimeProvider.GetLocalNow()` запрещены везде. EF
    migrations, `bin`, `obj` и `Generated` не проверяются.

85. `CancellationTokenSentinels_Should_NotBeUsed_When_Declared` — в авторских
    C#-исходниках из `src`, `tests` и `tools` запрещены
    `CancellationToken.None`, `default(CancellationToken)` и `default`, если его
    целевой тип — `CancellationToken`. В том числе токен нельзя объявлять как
    optional-параметр со значением `default`: вызывающий код должен передавать
    фактический токен явно.

86. `CancellationTokenParameters_Should_BeUsed_When_Declared` — объявленный в
    реализованном методе `CancellationToken` должен использоваться; иначе
    параметр нужно удалить. Overrides и реализации внешних интерфейсов не
    проверяются на использование, поскольку удалить параметр из их сигнатуры
    нельзя.

87. `CancellationTokens_Should_BeForwarded_When_Available` — если вызываемый
    метод объявляет параметр `CancellationToken`, а токен уже доступен в текущей
    области видимости, его нужно передать явно.

88. `CancellationTokens_Should_BeAvailable_When_CancellableOperationIsInvoked`
    — если в production-коде из `src` вызываемый метод поддерживает
    `CancellationToken`, но токен не передан и недоступен в текущей области
    видимости, текущий метод должен получить токен параметром. Токен
    последовательно прокидывается по цепочке вызовов от точки входа до
    отменяемой операции.

## Оформление тестов

89. `FactsAndTheories_Should_DeclareDisplayName_When_ATestIsDeclared` — каждый
    `[Fact]` и `[Theory]` во всех тестовых проектах должен содержать
    `DisplayName`, заданный строковым литералом.

90. `DisplayNames_Should_DescribeTestCondition_When_ATestIsDeclared` —
    `DisplayName` записывается на английском по схеме
    `<subject> should <behavior> when <condition>`. Часть после `when` должна
    соответствовать условию из имени тестового метода после `_When_`.

91. `TestMethods_Should_FollowNamingConvention_When_ATestIsDeclared` — имя
    каждого тестового метода должно соответствовать шаблону
    `MethodName_Should_DoSomething_When_Condition`.

92. `TestMethods_Should_FollowArrangeActAssert_When_ATestIsDeclared` — тест
    должен иметь block body, как минимум две логические секции, разделённые
    пустой строкой, и assertion в последней секции.

## Дополнительные архитектурные гарантии

93. `DomainObjects_Should_DeclareOnlyPrivateConstructors_When_CreatedThroughFactories`
    — конкретные `IEntity` и `ValueObject` должны объявлять только private
    конструкторы и предоставлять создание через фабричные методы.

94. `InfrastructureImplementations_Should_BeRegisteredForDomainOrApplicationAbstractions_When_Declared`
    — каждая реализация абстракции Domain или Application из Infrastructure
    должна присутствовать в итоговом `IServiceCollection` модуля.

95. `DatabaseContexts_Should_BeRegistered_When_Declared` — каждый конкретный
    `DbContext` должен присутствовать в итоговом `IServiceCollection` модуля.

96. `EndpointMappings_Should_DeclareAuthorizationIntent_When_Mapped` — каждый
    route endpoint должен явно вызвать `RequireAuthorization()` или
    `AllowAnonymous()`, либо наследовать такое решение от своей endpoint group.

97. `EndpointNames_Should_BeUnique_When_Mapped` — итоговые имена endpoint,
    включая явно заданные через `WithName`, должны быть уникальны.

98. `EndpointRoutes_Should_BeUnique_When_Mapped` — сочетание API version, HTTP
    method и полного route endpoint group + endpoint должно быть уникально.

99. `BlockingAsyncCalls_Should_NotBeUsed_When_ProductionCodeIsDeclared` — в
     production-коде запрещены блокирующие вызовы `Task.Wait()`, `Task.Result`
     и `GetAwaiter().GetResult()`.

100. `AsyncVoidCallables_Should_NotBeDeclared_When_ProductionCodeIsDeclared` —
     методы, local functions и lambdas в production-коде не должны быть
     `async void`.

101. `NullForgivingAssignments_Should_TargetOnlyComplexValueObjects_When_UsedInDomainState`
     — присваивание `null!` в состоянии aggregate root и entity разрешено
     только для комплексного value object с несколькими значениями.

102. `CommandValidators_Should_UseDomainFactories_When_DomainConstraintsAreDeclared`
     — command validator не должен повторять бизнес-инварианты через встроенные
     сравнения, диапазоны или произвольные предикаты FluentValidation; проверки
     должны делегироваться доменным фабрикам.

103. `EntityConfigurationLengthLimits_Should_ReferenceDomainConstants_When_Declared`
     — `HasMaxLength` в entity configuration должен ссылаться на публичную
     доменную константу `MaxLength`, а не дублировать числовое ограничение.

104. `ReadDatabaseModelAggregateForeignKeys_Should_HaveBidirectionalNavigations_When_Declared`
     — внешний ключ на aggregate read model из того же модуля требует nullable
     reference navigation у зависимой модели и collection navigation у главной.

105. `EndpointMappings_Should_NotRepeatGroupAuthorization_When_AuthorizationMatches`
     — endpoint не должен повторять тот же `RequireAuthorization` или
     `AllowAnonymous`, который уже объявлен его endpoint group.

106. `ConfigurationOptions_Should_HaveRegisteredValidatorsInSameDirectory_When_Declared`
     — каждый конфигурационный `<Name>Options` с константой `SectionName`
     должен иметь зарегистрированный `<Name>OptionsValidator`, реализующий
     `IValidateOptions<TOptions>` и расположенный рядом с options-классом.

107. `ConfigurationOptions_Should_UseValidateOnStart_When_Registered` — каждый
     конфигурационный options-класс должен регистрироваться через
     `AddOptions<TOptions>()` с последующим вызовом `ValidateOnStart()`.

108. `ConfigurationOptions_Should_ResideInDedicatedOptionsSubdirectories_When_Declared`
     — каждый конфигурационный `<Name>Options` должен располагаться вместе со
     своим validator в выделенной папке `Options/<Name>/`.

Пункты 12, 42 и 66 проверяют наличие соответствующих тестовых методов по их
именам, а не факт выполнения production-кода. Фактическое покрытие измеряется
отдельно средствами code coverage в CI.
