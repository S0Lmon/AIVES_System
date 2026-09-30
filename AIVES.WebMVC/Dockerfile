FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY AIVES.WebMVC/AIVES.WebMVC.csproj AIVES.WebMVC/
RUN dotnet restore AIVES.WebMVC/AIVES.WebMVC.csproj

COPY . .
RUN dotnet publish AIVES.WebMVC/AIVES.WebMVC.csproj --configuration Release --output /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "AIVES.WebMVC.dll"]
