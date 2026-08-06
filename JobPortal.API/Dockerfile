FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ENV DOTNET_ENVIRONMENT=Production
ENV ASPNETCORE_ENVIRONMENT=Production
WORKDIR /src
COPY ["JobPortal.API/JobPortal.API.csproj", "JobPortal.API/"]
COPY ["JobPortal.Application/JobPortal.Application.csproj", "JobPortal.Application/"]
COPY ["JobPortal.Domain/JobPortal.Domain.csproj", "JobPortal.Domain/"]
COPY ["JobPortal.Persistence/JobPortal.Persistence.csproj", "JobPortal.Persistence/"]
COPY ["JobPortal.Shared/JobPortal.Shared.csproj", "JobPortal.Shared/"]
RUN dotnet restore "JobPortal.API/JobPortal.API.csproj"
COPY . .
WORKDIR "/src/JobPortal.API"
RUN dotnet build "JobPortal.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "JobPortal.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "JobPortal.API.dll"]
