FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
WORKDIR /src
COPY . .
RUN dotnet tool restore
RUN dotnet restore ApplyWise.sln --locked-mode
ARG SOURCE_REVISION_ID=container-local
RUN dotnet publish src/ApplyWise.Web/ApplyWise.Web.csproj -c Release -o /app/publish --no-restore -p:SourceRevisionId=$SOURCE_REVISION_ID

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
COPY --from=build /app/publish .
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/App_Data/Uploads/Resumes /app/App_Data/DataProtectionKeys \
    && chown -R $APP_UID:$APP_UID /app
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "ApplyWise.Web.dll"]
