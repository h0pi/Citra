# The Citra Worker (reads and indexes documents, runs evaluations, resets the demo).
# Built from the backend folder:  docker build -f src/Citra.Worker/Dockerfile backend
# Stage 1 restores and publishes with the SDK; stage 2 runs it on the small .NET runtime image as a non-root user.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first: the restore layer is reused until a dependency changes.
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/Citra.Domain/Citra.Domain.csproj src/Citra.Domain/
COPY src/Citra.Application/Citra.Application.csproj src/Citra.Application/
COPY src/Citra.Infrastructure/Citra.Infrastructure.csproj src/Citra.Infrastructure/
COPY src/Citra.Worker/Citra.Worker.csproj src/Citra.Worker/
RUN dotnet restore src/Citra.Worker/Citra.Worker.csproj

COPY src/ src/
RUN dotnet publish src/Citra.Worker/Citra.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app

# The uploaded files the API stored (same volume); the folder belongs to the app user so a new volume does too.
RUN mkdir -p /data/storage && chown "$APP_UID" /data/storage
ENV Storage__LocalRootPath=/data/storage

COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Citra.Worker.dll"]
