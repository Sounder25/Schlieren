# Schlieren Engine Container
# Runs the CLI in 'node' mode as the RPC server

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copy pre-published CLI (which hosts RPC)
COPY publish/ .

# Expose JSON-RPC port
EXPOSE 8545

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8545/health || exit 1

# Run the CLI in node mode (starts RPC server on :8545)
ENTRYPOINT ["dotnet", "Schlieren.CLI.dll", "node", "--silent"]
