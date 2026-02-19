FROM mcr.microsoft.com/dotnet/sdk:8.0 AS docs-env

WORKDIR /docs
COPY osu.Server.Spectator ./osu.Server.Spectator
COPY osu.Server.Spectator.PublicAPIDocs ./osu.Server.Spectator.PublicAPIDocs
RUN dotnet build -c Release ./osu.Server.Spectator.PublicAPIDocs/osu.Server.Spectator.PublicAPIDocs.csproj

WORKDIR /docs/osu.Server.Spectator.PublicAPIDocs/out
RUN dotnet osu.Server.Spectator.PublicAPIDocs.dll
RUN ls .

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env

WORKDIR /app

# Copy csproj and restore as distinct layers
COPY osu.Server.Spectator/*.csproj ./

RUN dotnet restore

# Copy everything else and build
COPY osu.Server.Spectator ./
COPY --from=docs-env /docs/osu.Server.Spectator.PublicAPIDocs/out/_site ./wwwroot/docs
RUN dotnet publish -c Release -o out
# get rid of bloat
RUN rm -rf ./out/runtimes ./out/osu.Game.Resources.dll

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build-env /app/out .
ENTRYPOINT ["dotnet", "osu.Server.Spectator.dll"]
