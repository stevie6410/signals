# base stages unchanged
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 6000

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# context is now ./src, so paths drop the leading "src/"
COPY ["SDHome.Api/SDHome.Api.csproj", "SDHome.Api/"]
COPY ["SDHome.Lib/SDHome.Lib.csproj", "SDHome.Lib/"]

RUN dotnet restore "./SDHome.Api/SDHome.Api.csproj"

# bring in rest of src tree
COPY . .

WORKDIR "/src/SDHome.Api"
RUN dotnet build "./SDHome.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SDHome.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SDHome.Api.dll"]
