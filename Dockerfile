# ── Stage 1: build ────────────────────────────────────────────────────────────
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build

ARG TARGETARCH
ARG GIT_TAG_VERSION=dev

WORKDIR /src
COPY ["AGC Management.csproj", "."]
RUN dotnet restore "AGC Management.csproj"

COPY . .

RUN case "${TARGETARCH}" in \
      "amd64") DOTNET_RID="linux-x64"   ;; \
      "arm64") DOTNET_RID="linux-arm64" ;; \
      *)       DOTNET_RID="linux-x64"   ;; \
    esac && \
    dotnet publish "AGC Management.csproj" \
      -c Release \
      -r "${DOTNET_RID}" \
      --self-contained false \
      -o /app/publish \
      --no-restore \
      -p:InformationalVer="${GIT_TAG_VERSION}"

# ── Stage 2: download DiscordChatExporter CLI ─────────────────────────────────
FROM alpine:3.21 AS dce

ARG TARGETARCH

RUN apk add --no-cache curl unzip

RUN case "${TARGETARCH}" in \
      "amd64") DCE_ARCH="x64"   ;; \
      "arm64") DCE_ARCH="arm64" ;; \
      *)       DCE_ARCH="x64"   ;; \
    esac && \
    curl -fsSL \
      "https://github.com/Tyrrrz/DiscordChatExporter/releases/latest/download/DiscordChatExporter.Cli.linux-${DCE_ARCH}.zip" \
      -o /tmp/dce.zip && \
    unzip /tmp/dce.zip -d /dce && \
    chmod +x /dce/DiscordChatExporter.Cli

# ── Stage 3: runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

WORKDIR /app

# SkiaSharp (rank cards) native dependencies
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
      libfontconfig1 \
      libfreetype6 \
      libglib2.0-0 \
      libharfbuzz0b \
      libpng16-16 && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
COPY --from=dce   /dce         ./tools/exporter/

RUN mkdir -p data/tickets/transcripts/Assets

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "AGC Management.dll"]
