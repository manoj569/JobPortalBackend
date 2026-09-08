FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ENV DOTNET_ENVIRONMENT=Production
ENV ASPNETCORE_ENVIRONMENT=Production
WORKDIR /src
COPY ["JobPortal.API/JobPortal.API.csproj", "JobPortal.API/"]
COPY ["JobPortal.Application/JobPortal.Application.csproj", "JobPortal.Application/"]
COPY ["JobPortal.Domain/JobPortal.Domain.csproj", "JobPortal.Domain/"]
COPY ["JobPortal.Persistence/JobPortal.Persistence.csproj", "JobPortal.Persistence/"]
COPY ["JobPortal.Persistence.Postgres/JobPortal.Persistence.Postgres.csproj", "JobPortal.Persistence.Postgres/"]
COPY ["JobPortal.Infrastructure/JobPortal.Infrastructure.csproj", "JobPortal.Infrastructure/"]
COPY ["JobPortal.Shared/JobPortal.Shared.csproj", "JobPortal.Shared/"]
RUN dotnet restore "JobPortal.API/JobPortal.API.csproj"
COPY . .
WORKDIR "/src/JobPortal.API"
RUN dotnet build "JobPortal.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "JobPortal.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS dotnet-runtime

FROM mcr.microsoft.com/playwright/dotnet:v1.62.0-noble AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
ENV CHROME_DEVEL_SANDBOX=/usr/local/sbin/chrome-devel-sandbox
COPY --from=dotnet-runtime /usr/share/dotnet /usr/share/dotnet
COPY --from=publish /app/publish .
RUN useradd --create-home --uid 10001 appuser
RUN install -d -o appuser -g appuser /app/Logs /app/App_Data/resumes \
    && chmod 0555 /app/.playwright/node/linux-x64/node \
    && chmod -R a-w /ms-playwright \
    && find /ms-playwright -type f -name chrome_sandbox -exec install -o root -g root -m 4755 {} /usr/local/sbin/chrome-devel-sandbox \; \
    && find /ms-playwright -type d -name chrome-headless-shell-linux64 -exec install -o root -g root -m 4755 /usr/local/sbin/chrome-devel-sandbox {}/chrome-sandbox \;
USER appuser
ENTRYPOINT ["dotnet", "JobPortal.API.dll"]
