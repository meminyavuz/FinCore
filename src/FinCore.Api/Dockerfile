
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["src/FinCore.Api/FinCore.Api.csproj", "src/FinCore.Api/"]
RUN dotnet restore "src/FinCore.Api/FinCore.Api.csproj"

COPY . .
WORKDIR /src/src/FinCore.Api
RUN dotnet publish "FinCore.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "FinCore.Api.dll"]
