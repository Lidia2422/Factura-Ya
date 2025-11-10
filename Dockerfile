# PASO 1: Usamos la imagen oficial de .NET para construir la aplicación
- FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
+ FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["api/FacturadorAPI.csproj", "api/"]

# Restauramos las dependencias
RUN dotnet restore "api/FacturadorAPI.csproj"

# Copiamos todos los archivos fuente
COPY . .
WORKDIR "/src/api"

# Publicamos la aplicación
RUN dotnet publish "FacturadorAPI.csproj" -c Release -o /app/publish --no-restore

# PASO 2: Usamos la imagen de ejecución (más pequeña)
- FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS final
+ FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Establecemos el punto de entrada para la aplicación
ENTRYPOINT ["dotnet", "FacturadorAPI.dll"]

# Exponemos el puerto
EXPOSE 8080