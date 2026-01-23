
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src


COPY . .


RUN dotnet restore src/AiMultiAgent.Mcp.Server/AiMultiAgent.Mcp.Server.csproj


RUN dotnet publish src/AiMultiAgent.Mcp.Server/AiMultiAgent.Mcp.Server.csproj \
    -c Release \
    -o /app/publish \
    --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:7244

EXPOSE 7244

ENTRYPOINT ["dotnet", "AiMultiAgent.Mcp.Server.dll"]
