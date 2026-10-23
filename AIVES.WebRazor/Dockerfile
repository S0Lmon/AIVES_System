FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY AIVES.WebRazor/AIVES.WebRazor.csproj AIVES.WebRazor/
COPY AIVES.BLL/AIVES.BLL.csproj AIVES.BLL/
COPY AIVES.DAL/AIVES.DAL.csproj AIVES.DAL/
COPY AIVES.DTO/AIVES.DTO.csproj AIVES.DTO/
RUN dotnet restore AIVES.WebRazor/AIVES.WebRazor.csproj

COPY . .
RUN dotnet publish AIVES.WebRazor/AIVES.WebRazor.csproj --configuration Release --output /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13 AS base
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# The offline voice models are mounted read-only at /app/App_Data/speech-models (scripts/download-speech-models.ps1).
RUN mkdir -p /app/recordings /app/App_Data/speech-models && chown $APP_UID /app/recordings

FROM base AS runtime
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "AIVES.WebRazor.dll"]
