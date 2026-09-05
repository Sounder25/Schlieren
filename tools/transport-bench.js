/**
 * transport-bench.js
 * Benchmarks two transport paths to AWS Guard:
 *   A) HTTP (port 18545) - new connection per request
 *   B) WebSocket (port 18546/ws) - persistent connection
 *
 * Usage:
 *   node tools/transport-bench.js [--host 44.204.133.16] [--runs 20] [--token 0xA0b86...]
 */

const http  = require('http');
const https = require('https');
const ws    = require('ws');

const args  = process.argv.slice(2);
const get   = (flag, def) => { const i = args.indexOf(flag); return i >= 0 ? args[i+1] : def; };

const HOST  = get('--host',  '44.204.133.16');
const RUNS  = parseInt(get('--runs',  '25'), 10);
const TOKEN = get('--token', '0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48'); // USDC - known SELL_SUCCESSFUL

const HTTP_PORT = 18545;
const WS_PORT   = 18546;
const RPC_BODY  = JSON.stringify({ token: TOKEN, rpc: 'http://127.0.0.1:8545' });

function percentile(arr, p) {
  const sorted = [...arr].sort((a, b) => a - b);
  const idx = Math.ceil((p / 100) * sorted.length) - 1;
  return sorted[Math.max(0, idx)];
}

function stats(label, samples) {
  const mean = Math.round(samples.reduce((a, b) => a + b, 0) / samples.length);
  const p50  = percentile(samples, 50);
  const p95  = percentile(samples, 95);
  const p99  = percentile(samples, 99);
  const min  = Math.min(...samples);
  const max  = Math.max(...samples);
  console.log(`\n=== ${label} ===`);
  console.log(`  Samples : ${samples.length}`);
  console.log(`  Mean    : ${mean} ms`);
  console.log(`  Min     : ${min} ms`);
  console.log(`  P50     : ${p50} ms`);
  console.log(`  P95     : ${p95} ms`);
  console.log(`  P99     : ${p99} ms`);
  console.log(`  Max     : ${max} ms`);
  console.log(`  Raw     : [${samples.join(', ')}]`);
}

// ── HTTP benchmark ─────────────────────────────────────────────────────────────
function httpRequest() {
  return new Promise((resolve, reject) => {
    const start = Date.now();
    const req = http.request({
      hostname: HOST,
      port: HTTP_PORT,
      path: '/',
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(RPC_BODY) }
    }, (res) => {
      let body = '';
      res.on('data', d => body += d);
      res.on('end', () => {
        const elapsed = Date.now() - start;
        try {
          const parsed = JSON.parse(body);
          if (parsed.error) { reject(new Error('Guard error: ' + JSON.stringify(parsed.error))); return; }
          resolve(elapsed);
        } catch(e) { reject(e); }
      });
    });
    req.on('error', reject);
    req.setTimeout(60000, () => { req.destroy(); reject(new Error('HTTP timeout')); });
    req.write(RPC_BODY);
    req.end();
  });
}

async function runHttpBench() {
  console.log(`\nHTTP benchmark: ${RUNS} requests to ${HOST}:${HTTP_PORT}`);
  const samples = [];
  // warm-up
  try { await httpRequest(); console.log('  warm-up done'); } catch(e) { console.log('  warm-up error:', e.message); }

  for (let i = 0; i < RUNS; i++) {
    try {
      const ms = await httpRequest();
      samples.push(ms);
      process.stdout.write(`  [${i+1}/${RUNS}] ${ms}ms\r`);
    } catch(e) {
      console.log(`  [${i+1}] ERROR: ${e.message}`);
    }
  }
  console.log();
  return samples;
}

// ── WebSocket benchmark ────────────────────────────────────────────────────────
async function runWsBench() {
  console.log(`\nWebSocket benchmark: ${RUNS} requests to ws://${HOST}:${WS_PORT}/ws (persistent connection)`);
  return new Promise((resolve, reject) => {
    const samples = [];
    let run = 0;
    let reqStart;

    const client = new ws.WebSocket(`ws://${HOST}:${WS_PORT}/ws`);

    client.on('open', () => {
      console.log('  WebSocket connected');
      sendNext();
    });

    function sendNext() {
      if (run >= RUNS) {
        client.close();
        return;
      }
      run++;
      reqStart = Date.now();
      client.send(RPC_BODY);
    }

    client.on('message', (data) => {
      const elapsed = Date.now() - reqStart;
      try {
        const msg = JSON.parse(data.toString());
        if (!msg.success) {
          console.log(`  [${run}] ERROR: ${msg.error}`);
        } else {
          samples.push(elapsed);
          process.stdout.write(`  [${run}/${RUNS}] ${elapsed}ms\r`);
        }
      } catch(e) {
        console.log(`  [${run}] PARSE ERROR: ${e.message}`);
      }
      sendNext();
    });

    client.on('error', (e) => {
      console.log('  WS error:', e.message);
      reject(e);
    });

    client.on('close', () => {
      console.log();
      resolve(samples);
    });
  });
}

// ── Also test concurrent HTTP ──────────────────────────────────────────────────
async function runConcurrentHttpBench(concurrency = 5, batches = 5) {
  console.log(`\nConcurrent HTTP benchmark: ${batches} batches of ${concurrency} parallel requests`);
  const allSamples = [];
  for (let b = 0; b < batches; b++) {
    const batchStart = Date.now();
    const promises = Array.from({length: concurrency}, () => httpRequest().catch(e => ({ error: e.message })));
    const results = await Promise.all(promises);
    const batchMs = Date.now() - batchStart;
    const good = results.filter(r => typeof r === 'number');
    const bad  = results.filter(r => typeof r !== 'number');
    allSamples.push(...good);
    console.log(`  batch ${b+1}: wall=${batchMs}ms, ok=${good.length}, err=${bad.length}`);
    if (bad.length) console.log('    errors:', bad.map(r => r.error).join('; '));
    // small gap between batches
    await new Promise(r => setTimeout(r, 1000));
  }
  return allSamples;
}

(async () => {
  console.log('=== Guard Transport Benchmark ===');
  console.log(`Host  : ${HOST}`);
  console.log(`Token : ${TOKEN}`);
  console.log(`Runs  : ${RUNS} per transport`);

  const httpSamples = await runHttpBench();
  const wsSamples   = await runWsBench();
  const concSamples = await runConcurrentHttpBench(5, 5);

  if (httpSamples.length) stats('HTTP (new connection per request)', httpSamples);
  if (wsSamples.length)   stats('WebSocket (persistent connection)', wsSamples);
  if (concSamples.length) stats('Concurrent HTTP (5 parallel / 5 batches)', concSamples);

  // Comparison
  if (httpSamples.length && wsSamples.length) {
    const httpP50 = percentile(httpSamples, 50);
    const wsP50   = percentile(wsSamples, 50);
    const ratio   = (httpP50 / wsP50).toFixed(1);
    console.log(`\n=== VERDICT ===`);
    console.log(`  WebSocket is ${ratio}x faster at P50 (${wsP50}ms vs ${httpP50}ms)`);
    console.log(`  Winner: ${wsP50 < httpP50 ? 'WebSocket' : 'HTTP'}`);
  }
})();
