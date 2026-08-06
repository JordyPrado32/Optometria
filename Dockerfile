# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["OptometriaApp/OptometriaApp.csproj", "OptometriaApp/"]
RUN dotnet restore "OptometriaApp/OptometriaApp.csproj"

COPY OptometriaApp/ OptometriaApp/
WORKDIR /src/OptometriaApp
RUN dotnet publish "OptometriaApp.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        fonts-dejavu-core \
        libgdiplus \
        tzdata \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    TZ=America/Guayaquil

EXPOSE 8080

COPY --from=build /app/publish .
RUN chown -R "$APP_UID:$APP_UID" /app

USER $APP_UID
ENTRYPOINT ["dotnet", "OptometriaApp.dll"]
