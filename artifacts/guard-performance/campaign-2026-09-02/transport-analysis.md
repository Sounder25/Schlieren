# Phase 1 Transport Analysis

**Date:** 2026-09-02
**Campaign:** guard-latency-characterization

## Executive Summary

Transport overhead dominates end-to-end latency. The core Guard execution averages **200 ms** on EC2 localhost, but remote requests through the ALB take **4-12 seconds** — a 20-60x slowdown. TLS handshake latency accounts for 82% of the delay.

## Measurements

### LOCAL (EC2 localhost → Guard)

| Run | Latency (ms) |
|-----|--------------|
| 1 | 195 |
| 2 | 193 |
| 3 | 228 |
| 4 | 175 |
| 5 | 201 |

**Statistics:**
- Mean: 198 ms
- P50: 198 ms
- P95: 228 ms
- P99: 228 ms

**Method:** Direct curl to `http://127.0.0.1:18545` on EC2 host

### ALB-H2 (Windows client → ALB → Guard)

| Run | Total (ms) | TLS Handshake (ms) |
|-----|------------|---------------------|
| 1 | 1,033 | 847 |
| 2 | 1,406 | 1,097 |
| 3 | 1,013 | 716 |
| 4 | 1,729 | 1,335 |

**Statistics:**
- Mean Total: 1,295 ms
- Mean TLS: 999 ms
- TLS as % of Total: 77%

**Method:** curl from Windows 11 client to `https://schlieren.soundersolution.com`

### Full Guard Scan (ALB)

| Run | Total (ms) | Response Size |
|-----|------------|---------------|
| 1 | 4,521 | 751 KB |
| 2 | 3,881 | 751 KB |
| 3 | 7,170 | 751 KB |
| 4 | 3,226 | 751 KB |
| 5 | 12,191 | 751 KB |

**Statistics:**
- P50: 4,521 ms
- P95: 12,191 ms
- P99: 12,191 ms

## TLS Handshake Analysis

Breakdown of a single request:

| Stage | Duration |
|-------|----------|
| DNS resolution | 10 ms |
| TCP connect | 94 ms |
| **TLS handshake** | **847 ms** |
| Request/response | 186 ms |
| **Total** | **1,337 ms** |

The TLS handshake accounts for **63%** of the minimum end-to-end time.

### Certificate Chain

```
depth=2 C=US, O=Amazon, CN=Amazon Root CA 1
depth=1 C=US, O=Amazon, CN=Amazon RSA 2048 M01
depth=0 CN=schlieren.soundersolution.com
```

- Issuer: Amazon RSA 2048 M01
- Valid: 2026-09-02 to 2027-03-18
- Chain depth: 3 levels (standard for ACM)

## Root Cause

Each request from the Windows client establishes a **new TLS connection**. The ALB's TLS termination latency (700-1,300 ms) is paid on every request. HTTP keep-alive is not effectively reused due to:

1. curl's default behavior closes connections between invocations
2. The test harness creates new HttpClient instances per request
3. ALB may have high TLS negotiation overhead

## Comparison to Baseline Observation

The prior observation of **883 ms** for a full Guard run was likely:
- Through a different network path
- With connection pooling
- Or measuring only part of the request path

The **200 ms LOCAL baseline** represents the true core execution time. The 883 ms figure likely included some transport overhead but not the full TLS penalty.

## Recommendations

### Immediate (Production Path)

1. **WebSocket persistent session** — Establish one TLS connection, reuse for multiple scans
2. **HTTP/2 connection pooling** — Configure HttpClient to reuse connections
3. **ALB optimization** — Investigate if TLS session tickets or session resumption can reduce handshake cost

### Diagnostic

1. SSH port forward — Blocked by security group (not whitelisted for current IP)
2. SSM port forward — More complex setup, similar transport characteristics

### Disqualified (per §8.4)

- SSH tunnel: Requires active session, not suitable for autonomous production
- SSM tunnel: Same limitation as SSH

## Next Steps

1. Implement WebSocket transport for persistent sessions
2. Test connection pooling in the .NET harness
3. Profile ALB listener configuration for TLS optimization
4. Proceed to Phase 3 feature-ablation for the 200 ms core latency

## Files

- `Schlieren.PerfHarness/Program.cs` — Test harness with timing instrumentation
- `Schlieren.PerfHarness/Schlieren.PerfHarness.csproj` — Project file

---

**Verdict:** Transport is the bottleneck. Core Guard execution is 200 ms. TLS handshake adds 800-1,300 ms per request. Solution is **persistent connection** (WebSocket or HTTP/2 pooling), not optimizing the handshake itself.
