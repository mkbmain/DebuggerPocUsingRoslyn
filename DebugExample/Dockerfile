FROM mcr.microsoft.com/dotnet/core/runtime:3.1 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
WORKDIR /src
COPY ["debug/debug.csproj", "debug/"]
RUN dotnet restore "debug/debug.csproj"
COPY . .
WORKDIR "/src/debug"
RUN dotnet build "debug.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "debug.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "debug.dll"]
