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

FROM node:24.19.0-bookworm-slim AS content-node
WORKDIR /content
COPY tools/content_compiler/package.json ./
RUN npm install --omit=dev --ignore-scripts

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS runtime
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl libstdc++6 python3 python3-venv \
    && python3 -m venv /opt/lms-python \
    && /opt/lms-python/bin/pip install --no-cache-dir \
        PyYAML==6.0.3 markdown-it-py==3.0.0 Pillow==12.1.1 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
COPY --from=content-node /usr/local/bin/node /usr/local/bin/node
COPY --from=content-node /content/node_modules/katex /opt/lms-source/src/Lms.Web/node_modules/katex
COPY tools/content_compiler /opt/lms-source/tools/content_compiler
COPY tools/content_import /opt/lms-source/tools/content_import
ENV ASPNETCORE_HTTP_PORTS=8080
RUN mkdir -p /var/lib/lms/attachments /var/lib/lms/content-import /var/lib/lms/data-protection \
    && chown -R $APP_UID:$APP_UID /var/lib/lms
USER $APP_UID
EXPOSE 8443
ENTRYPOINT ["dotnet", "Lms.Api.dll"]
