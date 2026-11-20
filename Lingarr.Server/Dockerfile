# Set default build configuration
ARG BUILD_CONFIGURATION=Release

FROM --platform=$BUILDPLATFORM node:24-slim AS client
ARG BUILD_CONFIGURATION

# Step 1: Prepare Client
COPY ./Lingarr.Client/ /Client
WORKDIR /Client
# Install dependencies
RUN npm ci
RUN npm run build

# Step 2: Prepare the base image for the server
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
ARG TARGETARCH
WORKDIR /app
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ffmpeg \
        tesseract-ocr \
        tesseract-ocr-eng \
        ccextractor \
        libfontconfig1 \
        fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*

# Picture subtitles and captions are converted by tools shipped in the image.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS tools
ARG TARGETARCH
# SubtitleEdit is pinned: cloning its default branch means an upstream commit can break this image
# with no change of ours, which is exactly what happened on 2026-11-20 (seconv moved the Spectre
# overrides back to protected, and the sed below then made them public overrides). This commit is
# the last one verified to compile with the toolchain and the rewrite in this stage.
ARG SUBTITLEEDIT_SHA=6f5642783205b646c7215bc2ec78375907ed7826
RUN apt-get update \
    && apt-get install -y --no-install-recommends git \
    && rm -rf /var/lib/apt/lists/* \
    && git init -q /src/subtitleedit \
    && cd /src/subtitleedit \
    && git remote add origin https://github.com/SubtitleEdit/subtitleedit.git \
    && git fetch -q --depth 1 origin "$SUBTITLEEDIT_SHA" \
    && git checkout -q FETCH_HEAD
WORKDIR /src/subtitleedit
# seconv on main overrides Spectre commands as protected; this SDK requires public.
RUN find src/seconv -name '*.cs' -print0 | xargs -0 sed -i 's/protected override/public override/g'
RUN rid=$(if [ "$TARGETARCH" = "arm64" ]; then echo linux-arm64; else echo linux-x64; fi) \
    && dotnet publish src/seconv/SeConv.csproj -c Release -o /out/seconv -r "$rid" --self-contained false

# Step 3: Build the server
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION
ARG TARGETARCH
ARG VERSION=0.0.0-dev
WORKDIR /src
COPY ["Directory.Packages.props", "global.json", "./"]
COPY ["Lingarr.Server/Lingarr.Server.csproj", "Lingarr.Server/"]
COPY ["Lingarr.Core/Lingarr.Core.csproj", "Lingarr.Core/"]
COPY ["Lingarr.Migrations/Lingarr.Migrations.csproj", "Lingarr.Migrations/"]
RUN dotnet restore "./Lingarr.Server/./Lingarr.Server.csproj" --arch $TARGETARCH
COPY . .
WORKDIR "/src/Lingarr.Server"
COPY --from=client /Client/dist/ ./wwwroot
RUN dotnet build "./Lingarr.Server.csproj" -c $BUILD_CONFIGURATION -o /app/build --arch $TARGETARCH /p:Version=$VERSION

# Step 4: Publish the application
FROM --platform=$BUILDPLATFORM build AS publish
ARG BUILD_CONFIGURATION
ARG TARGETARCH
ARG VERSION=0.0.0-dev
RUN dotnet publish "./Lingarr.Server.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --arch $TARGETARCH /p:Version=$VERSION

# Step 5: Final image
FROM base AS final
WORKDIR /app
COPY --from=tools /out/seconv /opt/seconv
RUN ln -s /opt/seconv/seconv /usr/local/bin/seconv
COPY --from=publish /app/publish .

VOLUME /app/config
# Start the application
ENTRYPOINT ["dotnet", "Lingarr.Server.dll"]