FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY Sensor/Sensor.csproj Sensor/
COPY Shared/ Shared/
RUN dotnet restore Sensor/Sensor.csproj

COPY Sensor/ Sensor/
RUN dotnet publish Sensor/Sensor.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0

WORKDIR /app
COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "Sensor.dll"]
