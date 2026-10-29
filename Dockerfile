FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY NuGet.Config EventTicketing.slnx ./
COPY EventTicketing.Domain/EventTicketing.Domain.csproj EventTicketing.Domain/
COPY EventTicketing.Application/EventTicketing.Application.csproj EventTicketing.Application/
COPY EventTicketing.Infrastructure/EventTicketing.Infrastructure.csproj EventTicketing.Infrastructure/
COPY EventTicketing.Api/EventTicketing.Api.csproj EventTicketing.Api/

RUN dotnet restore EventTicketing.Api/EventTicketing.Api.csproj

COPY . .
RUN dotnet publish EventTicketing.Api/EventTicketing.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

COPY --from=build /app/publish ./

USER $APP_UID

ENTRYPOINT ["/bin/sh", "-c", "exec dotnet EventTicketing.Api.dll --urls http://0.0.0.0:${PORT:-8080}"]
