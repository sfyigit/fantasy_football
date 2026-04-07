FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/Shared/Shared.csproj                       src/Shared/
COPY src/LeaderboardWorker/LeaderboardWorker.csproj src/LeaderboardWorker/

RUN dotnet restore src/LeaderboardWorker/LeaderboardWorker.csproj

COPY src/Shared/            src/Shared/
COPY src/LeaderboardWorker/ src/LeaderboardWorker/

RUN dotnet publish src/LeaderboardWorker/LeaderboardWorker.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
EXPOSE 8081
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LeaderboardWorker.dll"]
