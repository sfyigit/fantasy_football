FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files first to leverage Docker layer caching for restore
COPY src/Shared/Shared.csproj                             src/Shared/
COPY src/MatchDataIngestion/MatchDataIngestion.csproj     src/MatchDataIngestion/

RUN dotnet restore src/MatchDataIngestion/MatchDataIngestion.csproj

# Copy source
COPY src/Shared/           src/Shared/
COPY src/MatchDataIngestion/ src/MatchDataIngestion/

RUN dotnet publish src/MatchDataIngestion/MatchDataIngestion.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MatchDataIngestion.dll"]
