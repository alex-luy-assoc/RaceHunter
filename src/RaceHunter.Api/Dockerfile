FROM node:22-alpine@sha256:c610fcdfb1d5b4740dd70c284ed3cb16bb857e0f7166196e36a5501df7a3aa32 AS web-build
WORKDIR /src/web
COPY src/RaceHunter.Web/package*.json ./
RUN npm ci
COPY src/RaceHunter.Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.400@sha256:e1ffd2a92ae84c1291bc1b6887501f8af98e6331e7af6d4c8d37168c5e87a64c AS dotnet-build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props RaceHunter.slnx ./
COPY src/ ./src/
RUN dotnet restore src/RaceHunter.Api/RaceHunter.Api.csproj
RUN dotnet publish src/RaceHunter.Api/RaceHunter.Api.csproj -c Release -o /app/publish --no-restore
COPY --from=web-build /src/web/dist/ /app/publish/wwwroot/

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:c4b29bf368004ad9076c1ab9bc91fb373561e3905b4345637e14e8b8c57e3be8 AS final
WORKDIR /app
RUN apk add --no-cache krb5-libs && addgroup -S racehunter && adduser -S racehunter -G racehunter
COPY --from=dotnet-build --chown=racehunter:racehunter /app/publish/ ./
USER racehunter
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "RaceHunter.Api.dll"]
