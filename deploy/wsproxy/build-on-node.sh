#!/bin/bash
set -e

# Build WSProxy on the EC2 node itself
# The repo is already cloned at /opt/schlieren

echo "=== Building WebSocket Proxy on EC2 ==="

cd /opt/schlieren

# Pull latest
sudo git fetch origin
sudo git checkout perf/guard-latency-characterization
sudo git pull origin perf/guard-latency-characterization

# Build
cd Schlieren.WSProxy
sudo dotnet build -c Release

# Create service directory
sudo mkdir -p /opt/schlieren-wsproxy
sudo cp bin/Release/net8.0/* /opt/schlieren-wsproxy/ -r

# Create systemd service
sudo tee /etc/systemd/system/schlieren-wsproxy.service > /dev/null << 'EOF'
[Unit]
Description=Schlieren Guard WebSocket Proxy
After=network.target

[Service]
Type=simple
User=ubuntu
WorkingDirectory=/opt/schlieren-wsproxy
ExecStart=/usr/local/bin/dotnet Schlieren.WSProxy.dll --urls "http://0.0.0.0:18546"
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable schlieren-wsproxy
sudo systemctl restart schlieren-wsproxy

echo "=== Checking service status ==="
sleep 3
sudo systemctl status schlieren-wsproxy --no-pager

echo "=== Health check ==="
curl -sf http://127.0.0.1:18546/health 2>/dev/null && echo " - WSProxy is UP" || echo "WSProxy not responding yet"
