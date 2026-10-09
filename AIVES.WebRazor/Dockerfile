FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY AIVES.WebRazor/AIVES.WebRazor.csproj AIVES.WebRazor/
COPY AIVES.BLL/AIVES.BLL.csproj AIVES.BLL/
COPY AIVES.DAL/AIVES.DAL.csproj AIVES.DAL/
COPY AIVES.DTO/AIVES.DTO.csproj AIVES.DTO/
RUN dotnet restore AIVES.WebRazor/AIVES.WebRazor.csproj

COPY . .
RUN dotnet publish AIVES.WebRazor/AIVES.WebRazor.csproj --configuration Release --output /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Whisper.net's Linux libraries need OpenMP; without it the native load fails and whisper.cpp aborts
# the process. With no Vulkan driver in the container it falls back to the CPU build on its own.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgomp1 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
# Speech models are mounted read-only at /app/App_Data/speech-models (scripts/download-speech-models.ps1).
RUN mkdir -p /app/recordings /app/App_Data/speech-models && chown $APP_UID /app/recordings
USER $APP_UID
ENTRYPOINT ["dotnet", "AIVES.WebRazor.dll"]
