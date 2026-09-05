/**
 * transport-bench-v2.js
 * More rigorous:
 *   1. Verify HTTP SG exposure
 *   2. WS with realistic pacing (2s between requests)
 *   3. WS cold (reconnect per request) vs persistent
 *   4. Collect Guard internal timing if present in response
 *   5. Degradation probe: fast vs paced comparison
 */

const http = require('http');
const ws   = require('ws');

const args = process.argv.slice(2);
const get  = (flag, def) => { const i = args.indexOf(flag); return i >= 0 ? args[i+1] : def; };

const HOST  = get('--host',  '44.204.133.16');
const TOKEN = get('--token', '0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48');

const WS_PORT   = 18546;
const HTTP_PORT = 18545;
const RPC_BODY  = JSON.stringify({ token: TOKEN, rpc: 'http://127.0.0.1:8545' });

// Schlieren JSON-RPC format (what 18545 actually expects)
const JSONRPC_BODY = JSON.stringify({
  jsonrpc: '2.0',
  id: 1,
  method: 'schlieren_guard',
  params: [TOKEN, 'http://127.0.0.1:8545']
});

function sleep(ms) { return new Promise(r => setTimeout(r, ms)); }

function percentile(arr, p) {
  const sorted = [...arr].sort((a, b) => a - b);
  const idx = Math.ceil((p / 100) * sorted.length) - 1;
  return sorted[Math.max(0, idx)];
}

function stats(label, samples) {
  if (!samples.length) { console.log(`\n=== ${label} ===\n  NO DATA`); return; }
  const mean = Math.round(samples.reduce((a, b) => a + b, 0) / samples.length);
  console.log(`\n=== ${label} ===`);
  console.log(`  n=${samples.length}  mean=${mean}ms  min=${Math.min(...samples)}ms  P50=${percentile(samples,50)}ms  P95=${percentile(samples,95)}ms  P99=${percentile(samples,99)}ms  max=${Math.max(...samples)}ms`);
  // print individual for trend analysis
  console.log(`  per-run: ${samples.map((v,i)=>`[${i+1}]${v}`).join(' ')}`);
}

// ── 1. HTTP port probe (quick - 5s timeout) ────────────────────────────────────
function httpProbe(port, body) {
  return new Promise((resolve) => {
    const start = Date.now();
    const req = http.request({ hostname: HOST, port, path: '/', method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) }
    }, (res) => {
      let b = '';
      res.on('data', d => b += d);
      res.on('end', () => resolve({ ok: true, ms: Date.now()-start, status: res.statusCode, body: b.slice(0, 200) }));
    });
    req.on('error', e => resolve({ ok: false, ms: Date.now()-start, error: e.message }));
    req.setTimeout(5000, () => { req.destroy(); resolve({ ok: false, ms: 5000, error: 'timeout' }); });
    req.write(body);
    req.end();
  });
}

// ── 2. WS request (single, connection passed in) ──────────────────────────────
function wsRequest(client) {
  return new Promise((resolve, reject) => {
    const start = Date.now();
    const handler = (data) => {
      const elapsed = Date.now() - start;
      try {
        const msg = JSON.parse(data.toString());
        resolve({ ok: msg.success, ms: elapsed, verdict: msg.result?.verdict?.headline, error: msg.error,
          guardMs: msg.result?.timingMs  // if Guard reports internal timing
        });
      } catch(e) { reject(e); }
    };
    client.once('message', handler);
    client.send(RPC_BODY);
  });
}

// ── 3. WS cold request (fresh connection per request) ─────────────────────────
function wsColdRequest() {
  return new Promise((resolve, reject) => {
    const connStart = Date.now();
    const client = new ws.WebSocket(`ws://${HOST}:${WS_PORT}/ws`);
    client.on('open', () => {
      const connMs = Date.now() - connStart;
      const reqStart = Date.now();
      client.send(RPC_BODY);
      client.once('message', (data) => {
        const elapsed = Date.now() - reqStart;
        const total   = Date.now() - connStart;
        try {
          const msg = JSON.parse(data.toString());
          resolve({ ok: msg.success, connMs, guardMs: elapsed, totalMs: total,
            verdict: msg.result?.verdict?.headline, error: msg.error });
          client.close();
        } catch(e) { reject(e); }
      });
    });
    client.on('error', reject);
    setTimeout(() => { client.terminate(); reject(new Error('cold-ws timeout')); }, 30000);
  });
}

// ── main ───────────────────────────────────────────────────────────────────────
(async () => {
  console.log('=== Guard Transport Benchmark v2 ===');
  console.log(`Target: ${HOST}  Token: ${TOKEN}\n`);

  // --- Test 1: HTTP port probe ---
  console.log('--- Test 1: HTTP port probe (5s timeout) ---');
  const h1 = await httpProbe(HTTP_PORT, JSONRPC_BODY);
  console.log(`  Port ${HTTP_PORT} (Schlieren RPC format): ${h1.ok ? `HTTP ${h1.status} in ${h1.ms}ms` : `FAIL: ${h1.error}`}`);
  const h2 = await httpProbe(HTTP_PORT, RPC_BODY);
  console.log(`  Port ${HTTP_PORT} (proxy format): ${h2.ok ? `HTTP ${h2.status} in ${h2.ms}ms` : `FAIL: ${h2.error}`}`);
  const h3 = await httpProbe(WS_PORT, RPC_BODY);
  console.log(`  Port ${WS_PORT} (HTTP, not WS): ${h3.ok ? `HTTP ${h3.status} in ${h3.ms}ms → ${h3.body}` : `FAIL: ${h3.error}`}`);

  // --- Test 2: WS persistent, fast (back-to-back, 0 gap) ---
  console.log('\n--- Test 2: WS persistent, fast (back-to-back, 0 gap), 10 runs ---');
  {
    const samples = [];
    const client = new ws.WebSocket(`ws://${HOST}:${WS_PORT}/ws`);
    await new Promise(r => client.on('open', r));
    for (let i = 0; i < 10; i++) {
      try {
        const r = await wsRequest(client);
        console.log(`  [${i+1}] ${r.ms}ms  verdict=${r.verdict||r.error}`);
        samples.push(r.ms);
      } catch(e) { console.log(`  [${i+1}] ERROR: ${e.message}`); }
    }
    client.close();
    stats('WS persistent FAST (0ms gap)', samples);
  }

  // --- Test 3: WS persistent, realistic pacing (2s gap) ---
  console.log('\n--- Test 3: WS persistent, paced (2s gap), 10 runs ---');
  {
    const samples = [];
    const client = new ws.WebSocket(`ws://${HOST}:${WS_PORT}/ws`);
    await new Promise(r => client.on('open', r));
    for (let i = 0; i < 10; i++) {
      try {
        const r = await wsRequest(client);
        console.log(`  [${i+1}] ${r.ms}ms  verdict=${r.verdict||r.error}`);
        samples.push(r.ms);
      } catch(e) { console.log(`  [${i+1}] ERROR: ${e.message}`); }
      if (i < 9) await sleep(2000);
    }
    client.close();
    stats('WS persistent PACED (2s gap)', samples);
  }

  // --- Test 4: WS cold (new connection per request), 8 runs ---
  console.log('\n--- Test 4: WS cold (new connection per request), 8 runs ---');
  {
    const connSamples = [], totalSamples = [], guardSamples = [];
    for (let i = 0; i < 8; i++) {
      try {
        const r = await wsColdRequest();
        console.log(`  [${i+1}] total=${r.totalMs}ms  conn=${r.connMs}ms  guard=${r.guardMs}ms  verdict=${r.verdict||r.error}`);
        connSamples.push(r.connMs);
        guardSamples.push(r.guardMs);
        totalSamples.push(r.totalMs);
      } catch(e) { console.log(`  [${i+1}] ERROR: ${e.message}`); }
      if (i < 7) await sleep(1000);
    }
    stats('WS COLD - connection overhead', connSamples);
    stats('WS COLD - guard execution (after connect)', guardSamples);
    stats('WS COLD - total (conn + guard)', totalSamples);
  }

  console.log('\n=== Done ===');
})();
