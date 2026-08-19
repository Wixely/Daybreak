# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
WORKDIR /source

COPY Directory.Build.props global.json Daybreak.slnx ./
COPY src/Daybreak/Daybreak.csproj src/Daybreak/
COPY src/Daybreak/packages.lock.json src/Daybreak/
COPY vendor/Nager.Date/src/Nager.Date/Nager.Date.csproj vendor/Nager.Date/src/Nager.Date/
# Re-evaluate the lock inside the pinned Linux SDK because .NET 10 otherwise
# derives the internal Blazor asset package from the host SDK patch.
RUN dotnet restore src/Daybreak/Daybreak.csproj --force-evaluate

COPY src/Daybreak/ src/Daybreak/
COPY vendor/Nager.Date/src/Nager.Date/ vendor/Nager.Date/src/Nager.Date/
RUN dotnet publish src/Daybreak/Daybreak.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__Daybreak="Data Source=/data/daybreak.db" \
    Daybreak__DataProtectionKeysPath=/data/keys \
    DAYBREAK_ADMIN_PASSWORD=daybreak
EXPOSE 8080
VOLUME ["/data"]

COPY --from=build /app/publish .
COPY --chmod=755 docker-entrypoint.sh /usr/local/bin/daybreak-entrypoint
RUN mkdir -p /data && chown -R $APP_UID:$APP_UID /data
ENTRYPOINT ["daybreak-entrypoint"]
CMD ["dotnet", "Daybreak.dll"]
