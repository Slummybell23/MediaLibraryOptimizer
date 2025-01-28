# ---------------------------------------
# 1) Base Image: .NET runtime with tools
# ---------------------------------------
FROM mcr.microsoft.com/dotnet/runtime:8.0-jammy AS base

# Set working directory
WORKDIR /app

# ---------------------------------------
# 2) Install Dependencies and FFmpeg
# ---------------------------------------
RUN apt-get update && \
    # Install essential packages
    apt-get install -y \
        ca-certificates \
        curl \
        gnupg \
        wget \
        software-properties-common \
        apt-transport-https \
        locales \
        nano \
        tar \
        mkvtoolnix \
        jq \
        sqlite3 \
        openssl && \
    # Install PowerShell
    wget -q https://packages.microsoft.com/config/debian/10/packages-microsoft-prod.deb && \
    dpkg -i packages-microsoft-prod.deb && \
    apt-get update && \
    apt-get install -y powershell && \
    # Download and install dovi_tool
    wget https://github.com/quietvoid/dovi_tool/releases/download/2.1.2/dovi_tool-2.1.2-x86_64-unknown-linux-musl.tar.gz && \
    tar -xvf dovi_tool-2.1.2-x86_64-unknown-linux-musl.tar.gz && \
    mv dovi_tool /usr/local/bin/dovi_tool && \
    chmod +x /usr/local/bin/dovi_tool && \
    rm -rf dovi_tool-2.1.2-x86_64-unknown-linux-musl.tar.gz packages-microsoft-prod.deb && \
    # ---------------------------------------
    # 3) Remove Existing FFmpeg (if any)
    # ---------------------------------------
    apt-get remove -y ffmpeg && \
    rm -f /usr/local/bin/ffmpeg /usr/local/bin/ffprobe && \
    # ---------------------------------------
    # 4) Download and Install FFmpeg from BtbN
    # ---------------------------------------
    # Define FFmpeg download URL based on specific version
    FFMPEG_URL="https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linux64-gpl.tar.xz" && \
    # Download FFmpeg
    wget "$FFMPEG_URL" -O /tmp/ffmpeg.tar.xz && \
    # Extract FFmpeg
    tar -xf /tmp/ffmpeg.tar.xz -C /tmp && \
    # Define variable for extracted directory
    EXTRACTED_DIR=$(tar -tf /tmp/ffmpeg.tar.xz | head -1 | cut -f1 -d"/") && \
    # Copy binaries to /usr/local/bin
    cp "/tmp/${EXTRACTED_DIR}/bin/ffmpeg" /usr/local/bin/ && \
    cp "/tmp/${EXTRACTED_DIR}/bin/ffprobe" /usr/local/bin/ && \
    # Ensure they are executable
    chmod +x /usr/local/bin/ffmpeg /usr/local/bin/ffprobe && \
    # Clean up
    rm -rf /tmp/ffmpeg.tar.xz "/tmp/${EXTRACTED_DIR}" && \

    # Final Cleanup to Reduce Image Size
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# ---------------------------------------
# 3) Build Stage: Compile the .NET App
# ---------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["LibraryOptimizer.csproj", "./"]
RUN dotnet restore "LibraryOptimizer.csproj"
COPY . ./
WORKDIR /src
RUN dotnet build "LibraryOptimizer.csproj" -c Release -o /app/build

# ---------------------------------------
# 4) Publish Stage: Publish the Compiled .NET App
# ---------------------------------------
FROM build AS publish
RUN dotnet publish "LibraryOptimizer.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ---------------------------------------
# 5) Final Runtime Stage
# ---------------------------------------
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Environment Variables
ENV NVIDIA_DRIVER_CAPABILITIES=all
ENV FORCE_START=n

# Run the App
ENTRYPOINT ["dotnet", "LibraryOptimizer.dll"]
