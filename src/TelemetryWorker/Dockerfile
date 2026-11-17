# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy project files
COPY ["src/Contracts/Contracts.csproj", "src/Contracts/"]
COPY ["src/TelemetryWorker/TelemetryWorker.csproj", "src/TelemetryWorker/"]

# Restore dependencies
RUN dotnet restore "src/TelemetryWorker/TelemetryWorker.csproj"

# Copy source and publish
COPY . .
WORKDIR "/source/src/TelemetryWorker"
RUN dotnet publish "TelemetryWorker.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TelemetryWorker.dll"]