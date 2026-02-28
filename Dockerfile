# ── Stage 1: build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG GIT_TAG_VERSION=dev

WORKDIR /src
COPY ["AGC Management.csproj", "."]
RUN dotnet restore "AGC Management.csproj" -r linux-x64

COPY . .

RUN dotnet publish "AGC Management.csproj" \
      -c Release \
      -r linux-x64 \
      --self-contained false \
      -o /app/publish \
      --no-restore \
      -p:InformationalVer="${GIT_TAG_VERSION}"

# ── Stage 2: download DiscordChatExporter CLI ─────────────────────────────────
FROM alpine:3.21 AS dce

RUN apk add --no-cache curl unzip

RUN curl -fsSL \
      "https://github.com/Tyrrrz/DiscordChatExporter/releases/latest/download/DiscordChatExporter.Cli.linux-x64.zip" \
      -o /tmp/dce.zip && \
    unzip /tmp/dce.zip -d /dce && \
    chmod +x /dce/DiscordChatExporter.Cli

# ── Stage 3: runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

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

EXPOSE 8085
ENV ASPNETCORE_URLS=http://+:8085

ENTRYPOINT ["dotnet", "AGC Management.dll"]
