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
        intel-media-va-driver-non-free \
        va-driver-all \
        echo "**** Install arch specific packages for $(uname -m) ****" \
            && sleep 2 \
            && \
            if uname -m | grep -q x86; then \
                echo "**** Add Intel Graphics repository  ****" \
                    && wget -qO - https://repositories.intel.com/gpu/intel-graphics.key | gpg --dearmor --output /usr/share/keyrings/intel-graphics.gpg \
                    && echo "deb [arch=amd64,i386 signed-by=/usr/share/keyrings/intel-graphics.gpg] https://repositories.intel.com/gpu/ubuntu jammy client" | tee /etc/apt/sources.list.d/intel-gpu-jammy.list \
                && \
                echo "**** Install Intel Media Drivers  ****" \
                    && apt-get update \
                    # intel-media-va-driver-non-free:
                    #   This is the primary driver for Intel's Video Acceleration API on Linux, which provides support for video encoding and decoding on Intel graphics hardware.
                    # libigdgmm12:
                    #   A part of the Intel Graphics Memory Management Library, which is used for managing graphics memory in an efficient and optimized way on Intel GPUs.
                    # libmfx1:
                    #   This is a library for Intel Media SDK runtime. It is necessary for applications that use Intel Quick Sync Video technology.
                    # libmfxgen1:
                    #   Similar to libmfx1, it's a part of the Intel Media SDK.
                    # libva-drm2 and libva2:
                    #   These are additional libraries needed for VAAPI to interface with the Direct Rendering Manager (DRM) and to provide the main API for VAAPI.
                    # libvpl2:
                    #   This is the successor to the Media SDK and provides a library for video processing.
                    && apt-get install -y \
                        intel-media-va-driver-non-free \
                        libigdgmm12 \
                        libmfx1 \
                        libmfxgen1 \
                        libva-drm2 \
                        libva2 \
                        libvpl2 \
                && \
                echo ; \
            fi \
        && \
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
