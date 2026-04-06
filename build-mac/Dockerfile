FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY . .
WORKDIR "/src/build-mac"

RUN apt-get update -y && \
    DEBIAN_FRONTEND=noninteractive apt-get install --no-install-recommends -y \
    zip && \
    rm -rf /var/lib/apt/lists/*

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN chmod a+x ./build.sh
RUN ./build.sh
RUN chmod a+x ./buildArm64.sh
RUN ./buildArm64.sh

ENTRYPOINT ["tail", "-f", "/dev/null"]
