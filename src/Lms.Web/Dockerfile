FROM node:24.19.0-alpine AS build
WORKDIR /source
RUN npm install --global pnpm@11.19.0
COPY src/Lms.Web/package.json src/Lms.Web/pnpm-lock.yaml src/Lms.Web/pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile
COPY src/Lms.Web/ ./
RUN pnpm build
FROM nginx:1.28-alpine AS runtime
COPY docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /source/dist /usr/share/nginx/html
EXPOSE 80
# Explicit Compose-only target adds a manual bearer-token adapter. Generic runtime stays clean.
FROM runtime AS local-development
COPY docker/local-login.html docker/local-auth.js /usr/share/nginx/html/
RUN sed -i 's@<head>@<head><script src="/local-auth.js"></script>@' /usr/share/nginx/html/index.html

FROM runtime AS production
