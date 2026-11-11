# The Citra API. Built from the backend folder:  docker build -f src/Citra.Api/Dockerfile backend
# Stage 1 restores and publishes with the SDK; stage 2 runs it on the small ASP.NET image as a non-root user.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first: the restore layer is reused until a dependency changes.
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/Citra.Domain/Citra.Domain.csproj src/Citra.Domain/
COPY src/Citra.Application/Citra.Application.csproj src/Citra.Application/
COPY src/Citra.Infrastructure/Citra.Infrastructure.csproj src/Citra.Infrastructure/
COPY src/Citra.Api/Citra.Api.csproj src/Citra.Api/
RUN dotnet restore src/Citra.Api/Citra.Api.csproj

COPY src/ src/
RUN dotnet publish src/Citra.Api/Citra.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Uploaded files live in a volume mounted here (shared with the Worker); the folder belongs to the app user so a
# new volume does too.
RUN mkdir -p /data/storage && chown "$APP_UID" /data/storage
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Storage__LocalRootPath=/data/storage

COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Citra.Api.dll"]
