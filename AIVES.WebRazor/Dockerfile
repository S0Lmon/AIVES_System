FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY AIVES.WebRazor/AIVES.WebRazor.csproj AIVES.WebRazor/
COPY AIVES.BLL/AIVES.BLL.csproj AIVES.BLL/
COPY AIVES.DAL/AIVES.DAL.csproj AIVES.DAL/
COPY AIVES.DTO/AIVES.DTO.csproj AIVES.DTO/
RUN dotnet restore AIVES.WebRazor/AIVES.WebRazor.csproj

COPY . .
RUN dotnet publish AIVES.WebRazor/AIVES.WebRazor.csproj --configuration Release --output /app/publish --no-restore /p:UseAppHost=false

# The NVIDIA image also ships whisper.cpp's CUDA build (~390 MB), so it is published separately.
FROM build AS publish-cuda
RUN dotnet publish AIVES.WebRazor/AIVES.WebRazor.csproj --configuration Release --output /app/publish /p:UseAppHost=false /p:WhisperCuda=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# Whisper.net's Linux libraries need OpenMP; without it the native load fails and whisper.cpp aborts
# the process. With no GPU runtime in the container it falls back to the CPU build on its own.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgomp1 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# Speech models are mounted read-only at /app/App_Data/speech-models (scripts/download-speech-models.ps1).
RUN mkdir -p /app/recordings /app/App_Data/speech-models && chown $APP_UID /app/recordings

# Optional NVIDIA image (compose.gpu.yaml): adds the CUDA 12 runtime and cuBLAS, about 850 MB.
# Whisper.net 1.8.1 is the last release built with CUDA 12 and sm_61, so it runs on a GTX 1070; 1.9.x needs CUDA 13
# and an RTX 30 or newer. Whisper.net probes CUDA by loading the unversioned libcudart.so, which only the -dev
# package ships, hence the symlink.
FROM base AS runtime-cuda
ARG CUDA_VERSION=12-9
RUN apt-get update \
    && apt-get install -y --no-install-recommends wget ca-certificates \
    && wget -q https://developer.download.nvidia.com/compute/cuda/repos/ubuntu2404/x86_64/cuda-keyring_1.1-1_all.deb \
    && dpkg -i cuda-keyring_1.1-1_all.deb && rm cuda-keyring_1.1-1_all.deb \
    && apt-get update \
    && apt-get install -y --no-install-recommends cuda-cudart-${CUDA_VERSION} libcublas-${CUDA_VERSION} \
    && apt-get purge -y wget && apt-get autoremove -y && rm -rf /var/lib/apt/lists/* \
    && ln -s libcudart.so.12 /usr/local/cuda/targets/x86_64-linux/lib/libcudart.so
ENV LD_LIBRARY_PATH=/usr/local/cuda/targets/x86_64-linux/lib
COPY --from=publish-cuda /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "AIVES.WebRazor.dll"]

# Default: CPU only. Kept last so a plain `docker build` / `docker compose build` produces it.
FROM base AS runtime
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "AIVES.WebRazor.dll"]
