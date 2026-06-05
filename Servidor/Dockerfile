FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY Servidor/Servidor.csproj Servidor/
COPY Shared/ Shared/
COPY proto/ proto/
RUN dotnet restore Servidor/Servidor.csproj

COPY Servidor/ Servidor/
RUN dotnet publish Servidor/Servidor.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0

WORKDIR /app
COPY --from=build /app/publish ./
RUN mkdir -p /app/data

EXPOSE 9090

ENTRYPOINT ["dotnet", "Servidor.dll", "9090"]
