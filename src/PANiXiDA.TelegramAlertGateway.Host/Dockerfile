# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["nuget.config", "."]
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["Directory.Build.targets", "."]
COPY ["src/PANiXiDA.TelegramAlertGateway.Host/PANiXiDA.TelegramAlertGateway.Host.csproj", "src/PANiXiDA.TelegramAlertGateway.Host/"]
COPY ["src/Module/PANiXiDA.TelegramAlertGateway.Notifications.Application/PANiXiDA.TelegramAlertGateway.Notifications.Application.csproj", "src/Module/PANiXiDA.TelegramAlertGateway.Notifications.Application/"]
COPY ["src/Module/PANiXiDA.TelegramAlertGateway.Notifications.Domain/PANiXiDA.TelegramAlertGateway.Notifications.Domain.csproj", "src/Module/PANiXiDA.TelegramAlertGateway.Notifications.Domain/"]
COPY ["src/Module/PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure/PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.csproj", "src/Module/PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure/"]
COPY ["src/Module/PANiXiDA.TelegramAlertGateway.Notifications.Presentation/PANiXiDA.TelegramAlertGateway.Notifications.Presentation.csproj", "src/Module/PANiXiDA.TelegramAlertGateway.Notifications.Presentation/"]
RUN dotnet restore "./src/PANiXiDA.TelegramAlertGateway.Host/PANiXiDA.TelegramAlertGateway.Host.csproj"
COPY . .
WORKDIR "/src/src/PANiXiDA.TelegramAlertGateway.Host"
RUN dotnet build "./PANiXiDA.TelegramAlertGateway.Host.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet run --project "./PANiXiDA.TelegramAlertGateway.Host.csproj" \
    -c $BUILD_CONFIGURATION \
    --no-restore \
    --no-launch-profile \
    -- codegen write
RUN dotnet publish "./PANiXiDA.TelegramAlertGateway.Host.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PANiXiDA.TelegramAlertGateway.Host.dll"]
