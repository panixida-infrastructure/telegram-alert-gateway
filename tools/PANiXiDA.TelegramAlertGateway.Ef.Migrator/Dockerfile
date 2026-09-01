# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["nuget.config", "."]
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["Directory.Build.targets", "."]
COPY ["tools/PANiXiDA.TelegramAlertGateway.Ef.Migrator/PANiXiDA.TelegramAlertGateway.Ef.Migrator.csproj", "tools/PANiXiDA.TelegramAlertGateway.Ef.Migrator/"]
COPY ["src/Notifications/PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure/PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.csproj", "src/Notifications/PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure/"]
COPY ["src/Notifications/PANiXiDA.TelegramAlertGateway.Notifications.Application/PANiXiDA.TelegramAlertGateway.Notifications.Application.csproj", "src/Notifications/PANiXiDA.TelegramAlertGateway.Notifications.Application/"]
COPY ["src/Notifications/PANiXiDA.TelegramAlertGateway.Notifications.Domain/PANiXiDA.TelegramAlertGateway.Notifications.Domain.csproj", "src/Notifications/PANiXiDA.TelegramAlertGateway.Notifications.Domain/"]
RUN dotnet restore "./tools/PANiXiDA.TelegramAlertGateway.Ef.Migrator/PANiXiDA.TelegramAlertGateway.Ef.Migrator.csproj"
COPY . .
WORKDIR "/src/tools/PANiXiDA.TelegramAlertGateway.Ef.Migrator"
RUN dotnet build "./PANiXiDA.TelegramAlertGateway.Ef.Migrator.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./PANiXiDA.TelegramAlertGateway.Ef.Migrator.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PANiXiDA.TelegramAlertGateway.Ef.Migrator.dll"]
