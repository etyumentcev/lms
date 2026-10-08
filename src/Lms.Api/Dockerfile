FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /source
COPY Directory.Build.props global.json ./
COPY src/Lms.Domain/Lms.Domain.csproj src/Lms.Domain/
COPY src/Lms.Application/Lms.Application.csproj src/Lms.Application/
COPY src/Lms.Infrastructure/Lms.Infrastructure.csproj src/Lms.Infrastructure/
COPY src/Lms.Api/Lms.Api.csproj src/Lms.Api/
RUN dotnet restore src/Lms.Api/Lms.Api.csproj
COPY src/Lms.Domain/ src/Lms.Domain/
COPY src/Lms.Application/ src/Lms.Application/
COPY src/Lms.Infrastructure/ src/Lms.Infrastructure/
COPY src/Lms.Api/ src/Lms.Api/
RUN dotnet publish src/Lms.Api/Lms.Api.csproj -c Release --no-restore -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS runtime
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Lms.Api.dll"]
