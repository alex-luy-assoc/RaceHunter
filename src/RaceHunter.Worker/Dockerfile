FROM mcr.microsoft.com/dotnet/sdk:10.0.400@sha256:e1ffd2a92ae84c1291bc1b6887501f8af98e6331e7af6d4c8d37168c5e87a64c AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props RaceHunter.slnx ./
COPY src/ ./src/
RUN dotnet restore src/RaceHunter.Worker/RaceHunter.Worker.csproj
RUN dotnet publish src/RaceHunter.Worker/RaceHunter.Worker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:c4b29bf368004ad9076c1ab9bc91fb373561e3905b4345637e14e8b8c57e3be8 AS final
WORKDIR /app
RUN addgroup -S racehunter && adduser -S racehunter -G racehunter
COPY --from=build --chown=racehunter:racehunter /app/publish/ ./
USER racehunter
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "RaceHunter.Worker.dll"]
