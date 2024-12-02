# Base image: .NET runtime with tools for running the app
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base

# Set working directory
WORKDIR /app

# Install dependencies
RUN apt-get update && apt-get install -y \
    wget tar ffmpeg mkvtoolnix && \
    wget https://github.com/quietvoid/dovi_tool/releases/download/2.1.2/dovi_tool-2.1.2-x86_64-unknown-linux-musl.tar.gz && \
    tar -xvf dovi_tool-2.1.2-x86_64-unknown-linux-musl.tar.gz && \
    mv dovi_tool /usr/local/bin/dovi_tool && \
    chmod +x /usr/local/bin/dovi_tool && \
    rm -rf dovi_tool-2.1.2-x86_64-unknown-linux-musl.tar.gz && \
    apt-get clean && rm -rf /var/lib/apt/lists/*

# Build stage: Compile the .NET app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["DolbyVisionProject.csproj", "./"]
RUN dotnet restore "DolbyVisionProject.csproj"
COPY . ./
WORKDIR /src
RUN dotnet build "DolbyVisionProject.csproj" -c Release -o /app/build

# Publish stage: Publish the compiled .NET app
FROM build AS publish
RUN dotnet publish "DolbyVisionProject.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final runtime stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Environment variables
ENV LIBRARY_PATH=/data
ENV CONFIG_PATH=/config
ENV CHECK_ALL=N
ENV ENCODE=N

# Run the app
ENTRYPOINT ["dotnet", "DolbyVisionProject.dll"]
