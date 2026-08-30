#!/bin/bash
set -euxo pipefail
export DEBIAN_FRONTEND=noninteractive

apt-get update
apt-get install -y --no-install-recommends ca-certificates curl jq openssl tar gzip

install -d -m 0755 /opt/eth /data/reth /data/lighthouse /etc/eth
openssl rand -hex 32 | tr -d '\n' > /etc/eth/jwt.hex
chmod 600 /etc/eth/jwt.hex

arch=aarch64-unknown-linux-gnu

reth_url=$(curl -fsSL https://api.github.com/repos/paradigmxyz/reth/releases/latest \
  | jq -r --arg arch "$arch" '.assets[] | select(.name | test($arch) and test("tar.gz$")) | .browser_download_url' \
  | head -n1)
curl -fsSL "$reth_url" -o /tmp/reth.tgz
tar -C /opt/eth -xzf /tmp/reth.tgz
install -m 0755 "$(find /opt/eth -type f -name reth | head -n1)" /usr/local/bin/reth

lh_url=$(curl -fsSL https://api.github.com/repos/sigp/lighthouse/releases/latest \
  | jq -r --arg arch "$arch" '.assets[] | select(.name | test($arch) and test("tar.gz$") and (test("portable") | not)) | .browser_download_url' \
  | head -n1)
curl -fsSL "$lh_url" -o /tmp/lighthouse.tgz
tar -C /opt/eth -xzf /tmp/lighthouse.tgz
install -m 0755 "$(find /opt/eth -type f -name lighthouse | head -n1)" /usr/local/bin/lighthouse

cat >/etc/systemd/system/reth.service <<'EOF'
[Unit]
Description=Reth execution client
After=network-online.target
Wants=network-online.target

[Service]
User=root
ExecStart=/usr/local/bin/reth node --chain mainnet --minimal --datadir /data/reth --http --http.addr 127.0.0.1 --http.port 8545 --http.api eth,net,web3 --ws --ws.addr 127.0.0.1 --authrpc.addr 127.0.0.1 --authrpc.port 8551 --authrpc.jwtsecret /etc/eth/jwt.hex --port 30303
Restart=always
RestartSec=5
LimitNOFILE=1048576

[Install]
WantedBy=multi-user.target
EOF

cat >/etc/systemd/system/lighthouse.service <<'EOF'
[Unit]
Description=Lighthouse beacon node
After=network-online.target reth.service
Wants=network-online.target

[Service]
User=root
ExecStart=/usr/local/bin/lighthouse bn --network mainnet --datadir /data/lighthouse --http --http-address 127.0.0.1 --execution-endpoint http://127.0.0.1:8551 --execution-jwt /etc/eth/jwt.hex --checkpoint-sync-url https://mainnet-checkpoint-sync.attestant.io --port 9000
Restart=always
RestartSec=5
LimitNOFILE=1048576

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable reth lighthouse
systemctl start reth lighthouse
