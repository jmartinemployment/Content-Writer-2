# Content Writer v2 API — Railway / Docker
# Build context: backend/ directory (docker build -f Dockerfile .)
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/ContentWriter.Api/ContentWriter.Api.csproj \
    -c Release \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "ContentWriter.Api.dll"]
