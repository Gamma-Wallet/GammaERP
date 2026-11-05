# Gamma ERP's screens: the Angular build served by Caddy, which also passes the API's paths to the erp container, so the screens and the
# API share one address (sign-in tokens are issued for it). Built from the repository root:
#   docker build -f deploy/web.Dockerfile -t gamma-erp-web .
# The Node major the lock file was written with (npm 11).
# The Angular build is the same for every processor, so it runs on the builder's own.
FROM --platform=$BUILDPLATFORM node:24-alpine AS build
WORKDIR /web
COPY src/web/package.json src/web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/web/ ./
RUN npx ng build --configuration production

FROM caddy:2.10-alpine
COPY deploy/Caddyfile /etc/caddy/Caddyfile
COPY --from=build /web/dist/web/browser /srv
# An unprivileged user of its own; Caddy keeps its state in /data and /config.
RUN addgroup -S web && adduser -S -G web web && chown -R web:web /data /config
USER web
ENV ERP_UPSTREAM=erp:8080
EXPOSE 8080
