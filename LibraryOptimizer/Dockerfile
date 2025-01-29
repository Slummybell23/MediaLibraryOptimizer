######################################################################
# 1) Build Stage: Use the .NET SDK to compile your application
######################################################################
FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
LABEL stage=build

WORKDIR /src

# Copy only the .csproj first for caching layers
COPY ["LibraryOptimizer.csproj", "./"]

# Restore dependencies
RUN dotnet restore "LibraryOptimizer.csproj"

# Now copy everything else and build
COPY . ./
RUN dotnet build "LibraryOptimizer.csproj" -c Release -o /app/build

######################################################################
# 2) Publish Stage: Publish the compiled output
######################################################################
FROM build AS publish
LABEL stage=publish

RUN dotnet publish "LibraryOptimizer.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

######################################################################
# 3) Final Runtime Stage: Lightweight .NET runtime + tools
######################################################################
FROM mcr.microsoft.com/dotnet/runtime:8.0-jammy AS final

# Avoid interactive prompts during package installs
ENV DEBIAN_FRONTEND=noninteractive

# Optional environment variables if you need them at runtime
ENV NVIDIA_DRIVER_CAPABILITIES=all
ENV FORCE_START=n

WORKDIR /app

# ---------------------------------------------------
# Install dependencies, PowerShell, dovi_tool, FFmpeg
# ---------------------------------------------------
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        gnupg \
        wget \
        intel-media-va-driver-non-free \
        software-properties-common \
        apt-transport-https \
        locales \
        nano \
        tar \
        xz-utils \
        mkvtoolnix \
        jq \
        sqlite3 \
        openssl && \
    \
    # Install PowerShell (for Ubuntu 22.04 / Jammy)
    wget -q https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O /tmp/ps.deb && \
    dpkg -i /tmp/ps.deb && rm /tmp/ps.deb && \
    apt-get update && \
    apt-get install -y --no-install-recommends powershell && \
    \
    # Install dovi_tool
    wget -q https://github.com/quietvoid/dovi_tool/releases/download/2.1.2/dovi_tool-2.1.2-x86_64-unknown-linux-musl.tar.gz -O /tmp/dovi_tool.tar.gz && \
    tar -xzf /tmp/dovi_tool.tar.gz -C /tmp && \
    mv /tmp/dovi_tool /usr/local/bin/dovi_tool && \
    chmod +x /usr/local/bin/dovi_tool && \
    rm -f /tmp/dovi_tool.tar.gz && \
    \
    # Download and install latest FFmpeg build from BtbN
    wget -q https://github.com/jellyfin/jellyfin-ffmpeg/releases/download/v7.0.2-9/jellyfin-ffmpeg_7.0.2-9_portable_linux64-gpl.tar.xz -O /tmp/ffmpeg.tar.xz && \
    mkdir -p /tmp/ffmpeg && \
    tar -xf /tmp/ffmpeg.tar.xz -C /tmp/ffmpeg && \
    cp /tmp/ffmpeg/ffmpeg /usr/local/bin/ && \
    cp /tmp/ffmpeg/ffprobe /usr/local/bin/ && \
    chmod +x /usr/local/bin/ffmpeg /usr/local/bin/ffprobe && \
    \
    # Cleanup to keep the image small
    rm -rf /tmp/ffmpeg /tmp/ffmpeg.tar.xz && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# ---------------------------------------------------
# Copy the published .NET binaries from the publish stage
# ---------------------------------------------------
COPY --from=publish /app/publish /app

# ---------------------------------------------------
# Set the entrypoint to run your .NET application
# ---------------------------------------------------
ENTRYPOINT ["dotnet", "LibraryOptimizer.dll"]
