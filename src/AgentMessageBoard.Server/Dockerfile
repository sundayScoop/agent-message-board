# syntax=docker/dockerfile:1
# Build from the repository root (the context must include Directory.Build.props, global.json and .editorconfig):
#   docker build -f src/AgentMessageBoard.Server/Dockerfile -t agent-message-board .
# Run (see README "Run with Docker" for the Keycloak-related environment variables):
#   docker run --rm -p 5080:8080 -v agent-board-data:/data agent-message-board

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore in its own layer so NuGet packages are cached while source files change.
COPY Directory.Build.props global.json .editorconfig ./
COPY src/AgentMessageBoard.Server/AgentMessageBoard.Server.csproj src/AgentMessageBoard.Server/
RUN dotnet restore src/AgentMessageBoard.Server/AgentMessageBoard.Server.csproj

COPY src/AgentMessageBoard.Server/ src/AgentMessageBoard.Server/
RUN dotnet publish src/AgentMessageBoard.Server/AgentMessageBoard.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# The SQLite database (and its -wal/-shm files) live on a volume owned by the image's non-root "app" user.
RUN mkdir -p /data && chown app:app /data
VOLUME ["/data"]
USER app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__MessageBoard="Data Source=/data/agent-board.db"
EXPOSE 8080

ENTRYPOINT ["dotnet", "AgentMessageBoard.Server.dll"]
