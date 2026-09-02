#!/bin/bash
set -e

# Install WebSocket proxy to AWS EC2
# Target: /opt/schlieren-wsproxy

echo "=== Installing Schlieren WebSocket Proxy ==="

# Create directory
sudo mkdir -p /opt/schlieren-wsproxy
sudo chown ubuntu:ubuntu /opt/schlieren-wsproxy

# Copy published files (assumes they're in /tmp/wsproxy-publish)
echo "Copying files..."
cp -r /tmp/wsproxy-publish/* /opt/schlieren-wsproxy/

# Create systemd service
echo "Creating systemd service..."
sudo tee /etc/systemd/system/schlieren-wsproxy.service > /dev/null << 'EOF'
[Unit]
Description=Schlieren Guard WebSocket Proxy
After=network.target

[Service]
Type=simple
User=ubuntu
WorkingDirectory=/opt/schlieren-wsproxy
ExecStart=/opt/schlieren-wsproxy/Schlieren.WSProxy --urls "http://0.0.0.0:18546"
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF

# Enable and start
sudo systemctl daemon-reload
sudo systemctl enable schlieren-wsproxy
sudo systemctl start schlieren-wsproxy

echo "=== Installation complete ==="
sudo systemctl status schlieren-wsproxy --no-pager
