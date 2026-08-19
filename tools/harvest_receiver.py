"""
Schlieren Harvest Receiver
Tiny HTTP server that accepts POSTs from n8n and writes to the corpus directory.
Runs on localhost:7891 — n8n POSTs candidates here from its Code node via HTTP.
"""

import http.server
import json
import os
import threading
from datetime import datetime

CORPUS_DIR = r"C:\projects\Schlieren\muscle\corpus"
PORT = 7891

class HarvestHandler(http.server.BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass  # suppress default logging

    def do_POST(self):
        length = int(self.headers.get('Content-Length', 0))
        body = self.rfile.read(length)

        try:
            data = json.loads(body)
            os.makedirs(CORPUS_DIR, exist_ok=True)

            if self.path == '/candidates':
                # WF-A sends scored candidates — write index file
                candidates = data.get('candidates', [])
                index = {
                    'scannedAt': data.get('scannedAt', datetime.utcnow().isoformat()),
                    'totalScored': data.get('totalScored', len(candidates)),
                    'candidates': candidates
                }
                path = os.path.join(CORPUS_DIR, 'harvest_index.json')
                with open(path, 'w', encoding='utf-8') as f:
                    json.dump(index, f, indent=2)
                resp = {'ok': True, 'written': len(candidates), 'path': path}
                print(f"[{datetime.now().strftime('%H:%M:%S')}] Received {len(candidates)} candidates -> {path}")

            elif self.path == '/fixture':
                # WF-B sends a completed fixture
                tx_hash = data.get('txHash', 'unknown').replace('0x', '')[:16]
                filename = f"{tx_hash}.json"
                path = os.path.join(CORPUS_DIR, filename)
                with open(path, 'w', encoding='utf-8') as f:
                    json.dump(data.get('fixture', data), f, indent=2)
                resp = {'ok': True, 'path': path}
                print(f"[{datetime.now().strftime('%H:%M:%S')}] Fixture written -> {filename}")

            else:
                resp = {'ok': False, 'error': f'Unknown path {self.path}'}

        except Exception as e:
            resp = {'ok': False, 'error': str(e)}
            print(f"[ERROR] {e}")

        body_out = json.dumps(resp).encode()
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', len(body_out))
        self.end_headers()
        self.wfile.write(body_out)

    def do_GET(self):
        if self.path == '/status':
            index_path = os.path.join(CORPUS_DIR, 'harvest_index.json')
            exists = os.path.exists(index_path)
            count = 0
            if exists:
                try:
                    with open(index_path) as f:
                        d = json.load(f)
                    count = len(d.get('candidates', []))
                except:
                    pass
            resp = {'running': True, 'corpus': CORPUS_DIR, 'index_exists': exists, 'candidates': count}
        else:
            resp = {'running': True}
        body_out = json.dumps(resp).encode()
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', len(body_out))
        self.end_headers()
        self.wfile.write(body_out)

if __name__ == '__main__':
    server = http.server.HTTPServer(('0.0.0.0', PORT), HarvestHandler)
    print(f"Schlieren Harvest Receiver running on port {PORT}")
    print(f"Corpus dir: {CORPUS_DIR}")
    print(f"Endpoints:")
    print(f"  POST http://host.docker.internal:{PORT}/candidates  — WF-A sends scored candidates")
    print(f"  POST http://host.docker.internal:{PORT}/fixture     — WF-B sends completed fixtures")
    print(f"  GET  http://localhost:{PORT}/status                 — health check")
    server.serve_forever()
