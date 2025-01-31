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
# 3) Final Runtime Stage: Lightweight .NET runtime + extra tools
######################################################################
FROM mcr.microsoft.com/dotnet/runtime:8.0-jammy AS final

# Avoid interactive prompts during package installs
ENV DEBIAN_FRONTEND=noninteractive

# Optional environment variables if you need them at runtime
ENV NVIDIA_DRIVER_CAPABILITIES=all
ENV FORCE_START=n

WORKDIR /app

# ---------------------------------------------------
# 1) Base dependencies
# ---------------------------------------------------
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        gnupg \
        wget \
        libexpat1 \
        libglib2.0-0 \
        libgomp1 \
        libharfbuzz0b \
        libmediainfo0v5 \
        libv4l-0 \
        libx11-6 \
        libxcb1 \
        libxext6 \
        libxml2 \
        va-driver-all \
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
    rm -rf /var/lib/apt/lists/*

# ---------------------------------------------------
# 2) (Conditional) Intel GPU packages for x86_64
# ---------------------------------------------------
RUN if uname -m | grep -q x86_64; then \
        echo "**** Installing Intel GPU drivers for x86_64 ****" && \
        wget -qO - https://repositories.intel.com/gpu/intel-graphics.key \
          | gpg --dearmor --output /usr/share/keyrings/intel-graphics.gpg && \
        echo "deb [arch=amd64,i386 signed-by=/usr/share/keyrings/intel-graphics.gpg] \
             https://repositories.intel.com/gpu/ubuntu jammy client" \
          | tee /etc/apt/sources.list.d/intel-gpu-jammy.list && \
        apt-get update && \
        apt-get install -y --no-install-recommends \
            intel-media-va-driver-non-free \
            libigdgmm12 \
            libmfx1 \
            libmfxgen1 \
            libva-drm2 \
            libva2 \
            libvpl2 && \
        rm -rf /var/lib/apt/lists/*; \
    fi

# ---------------------------------------------------
# 3) PowerShell
# ---------------------------------------------------
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
      wget \
      && \
    wget -q https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O /tmp/ps.deb && \
    dpkg -i /tmp/ps.deb && rm /tmp/ps.deb && \
    apt-get update && \
    apt-get install -y --no-install-recommends powershell && \
    rm -rf /var/lib/apt/lists/*

# ---------------------------------------------------
# 4) dovi_tool
# ---------------------------------------------------
RUN wget -q https://github.com/quietvoid/dovi_tool/releases/download/2.1.2/dovi_tool-2.1.2-x86_64-unknown-linux-musl.tar.gz -O /tmp/dovi_tool.tar.gz && \
    tar -xzf /tmp/dovi_tool.tar.gz -C /tmp && \
    mv /tmp/dovi_tool /usr/local/bin/dovi_tool && \
    chmod +x /usr/local/bin/dovi_tool && \
    rm -f /tmp/dovi_tool.tar.gz

# ---------------------------------------------------
# 5) FFmpeg (Jellyfin build)
# ---------------------------------------------------
RUN wget -q https://github.com/jellyfin/jellyfin-ffmpeg/releases/download/v7.0.2-9/jellyfin-ffmpeg_7.0.2-9_portable_linux64-gpl.tar.xz -O /tmp/ffmpeg.tar.xz && \
    mkdir -p /tmp/ffmpeg && \
    tar -xf /tmp/ffmpeg.tar.xz -C /tmp/ffmpeg && \
    cp /tmp/ffmpeg/ffmpeg /usr/local/bin/ && \
    cp /tmp/ffmpeg/ffprobe /usr/local/bin/ && \
    chmod +x /usr/local/bin/ffmpeg /usr/local/bin/ffprobe && \
    rm -rf /tmp/ffmpeg /tmp/ffmpeg.tar.xz

# ---------------------------------------------------
# 6) Copy published .NET app
# ---------------------------------------------------
COPY --from=publish /app/publish /app

# ---------------------------------------------------
# 7) Entrypoint
# ---------------------------------------------------
ENTRYPOINT ["dotnet", "LibraryOptimizer.dll"]
