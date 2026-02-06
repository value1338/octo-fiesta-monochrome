# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy source code and build
COPY . ./
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Create directories for downloads and cache
RUN mkdir -p /music /cache /config

# Copy published app
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:4533
ENV ASPNETCORE_ENVIRONMENT=Production
ENV Library__DownloadPath=/music
ENV Library__CachePath=/cache

# Expose port
EXPOSE 4533

# Run the application
ENTRYPOINT ["dotnet", "octo-fiesta.dll"]
