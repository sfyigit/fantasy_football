FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/Shared/Shared.csproj               src/Shared/
COPY src/ScoringWorker/ScoringWorker.csproj src/ScoringWorker/

RUN dotnet restore src/ScoringWorker/ScoringWorker.csproj

COPY src/Shared/       src/Shared/
COPY src/ScoringWorker/ src/ScoringWorker/

RUN dotnet publish src/ScoringWorker/ScoringWorker.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ScoringWorker.dll"]
