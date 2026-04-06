FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY . .
WORKDIR "/src/build-win"

FROM build AS publish

# https://developer.cyberark.com/blog/building-a-windows-installer-from-a-linux-ci-pipeline/
# https://github.com/albertowd/nsis/blob/main/Dockerfile
RUN dpkg --add-architecture i386 && apt-get update -y && \
    DEBIAN_FRONTEND=noninteractive apt-get install --no-install-recommends -y \
    nsis nsis-doc nsis-pluginapi \
    wget \
    software-properties-common apt-transport-https && \
    rm -rf /var/lib/apt/lists/*

ARG BUILD_CONFIGURATION=Release
RUN chmod a+x ./build.sh
RUN ./build.sh
RUN chmod a+x ./build32.sh
RUN ./build32.sh

ENTRYPOINT ["tail", "-f", "/dev/null"]
