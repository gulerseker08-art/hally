FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/src/Hally.Domain/Hally.Domain.csproj backend/src/Hally.Domain/
COPY backend/src/Hally.Infrastructure/Hally.Infrastructure.csproj backend/src/Hally.Infrastructure/
COPY backend/src/Hally.Api/Hally.Api.csproj backend/src/Hally.Api/
RUN dotnet restore backend/src/Hally.Api/Hally.Api.csproj
COPY backend/src backend/src
RUN dotnet publish backend/src/Hally.Api/Hally.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Hally.Api.dll"]
