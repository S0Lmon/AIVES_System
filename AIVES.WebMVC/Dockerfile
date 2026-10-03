FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY AIVES.WebMVC/AIVES.WebMVC.csproj AIVES.WebMVC/
COPY AIVES.BLL/AIVES.BLL.csproj AIVES.BLL/
COPY AIVES.DAL/AIVES.DAL.csproj AIVES.DAL/
COPY AIVES.DTO/AIVES.DTO.csproj AIVES.DTO/
RUN dotnet restore AIVES.WebMVC/AIVES.WebMVC.csproj

COPY . .
RUN dotnet publish AIVES.WebMVC/AIVES.WebMVC.csproj --configuration Release --output /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
# A fresh named volume copies this folder's owner, so the non-root app user can write recordings.
RUN mkdir -p /app/recordings && chown $APP_UID /app/recordings
USER $APP_UID
ENTRYPOINT ["dotnet", "AIVES.WebMVC.dll"]
