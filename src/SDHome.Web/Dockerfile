# ----- Build stage -----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project file, then restore
COPY Signals.sln ./
COPY src/SDHome.Web/SDHome.Web.csproj ./SDHome.Web/
RUN dotnet restore "SDHome.Web/SDHome.Web.csproj"

# Copy the rest of the source and build
COPY src/ ./ 
WORKDIR /src/SDHome.Web
RUN dotnet publish "SDHome.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ----- Runtime stage -----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Copy published output
COPY --from=build /app/publish .

# ASP.NET Core container images (.NET 8+) listen on 8080 by default
# https framework is optional; we'll just expose HTTP for now
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "SDHome.Web.dll"]
