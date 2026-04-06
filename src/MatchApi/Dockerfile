FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/Shared/Shared.csproj     src/Shared/
COPY src/MatchApi/MatchApi.csproj src/MatchApi/

RUN dotnet restore src/MatchApi/MatchApi.csproj

COPY src/Shared/  src/Shared/
COPY src/MatchApi/ src/MatchApi/

RUN dotnet publish src/MatchApi/MatchApi.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "MatchApi.dll"]
