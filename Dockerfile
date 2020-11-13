FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-env

COPY MySqlBackupAgent/ /app/MySqlBackupAgent/
WORKDIR /app/MySqlBackupAgent

# Restore
RUN dotnet restore

# Build
RUN dotnet publish -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/core/aspnet:3.1
WORKDIR /app
COPY --from=build-env /app/MySqlBackupAgent/out .
ENTRYPOINT ["dotnet", "MySqlBackupAgent.dll"]
