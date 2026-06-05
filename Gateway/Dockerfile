FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY Gateway/Gateway.csproj Gateway/
COPY Sensor/Sensor.csproj Sensor/
COPY Shared/ Shared/
COPY proto/ proto/
RUN dotnet restore Gateway/Gateway.csproj

COPY Gateway/ Gateway/
COPY Sensor/SensorMessage.cs Sensor/
RUN dotnet publish Gateway/Gateway.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0

WORKDIR /app
COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "Gateway.dll"]
