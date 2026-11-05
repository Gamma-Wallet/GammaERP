# Gamma ERP: the API and its database migrations in one image. Built from the repository root:
#   docker build -f deploy/Dockerfile -t gamma-erp .
# At start the container brings the database to the image's schema (the migrations bundle), then runs the API on port 8080.
# The build runs on the builder's own processor; only the migrations bundle is native, so it is built for the target (amd64 or arm64).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY .editorconfig global.json Directory.Build.props Directory.Packages.props dotnet-tools.json ./
COPY src/ src/
# The user guide: the API embeds it, so Claude can read it through the MCP server.
COPY .md/ .md/
RUN dotnet tool restore
RUN dotnet publish src/backend/Gamma.Erp.Api -c Release -o /out/api -p:UseAppHost=false
RUN case "$TARGETARCH" in arm64) RID=linux-arm64 ;; *) RID=linux-x64 ;; esac && \
    dotnet ef migrations bundle --project src/backend/Gamma.Erp.Migrations --startup-project src/backend/Gamma.Erp.Migrations \
      --context ErpDbContext --configuration Release --self-contained -r $RID -o /out/migrate

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out/api ./
COPY --from=build /out/migrate ./migrate
COPY deploy/entrypoint.sh ./entrypoint.sh
RUN chmod 0755 ./entrypoint.sh ./migrate
# The Kerberos library Npgsql looks for when it connects (not used for sign-in here; without it every start logs an error).
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
# /data: the files and the sign-in keys, a volume of the host.
RUN mkdir -p /data/files /data/keys && chown -R $APP_UID /data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    Files__Root=/data/files \
    DataProtection__KeysFolder=/data/keys
EXPOSE 8080
VOLUME /data
# The image's own unprivileged user.
USER $APP_UID
ENTRYPOINT ["/app/entrypoint.sh"]
