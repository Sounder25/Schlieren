import { useAppStore } from './store';

export interface ConformanceFailure {
  caseId: string;
  fixturePath: string;
  summary: string;
  mismatches: string[];
  primaryCategory: string;
  eipCluster: string;
  clusterKey: string;
  layer1Headline: string;
  layer1Body: string;
  gasUsed: number;
}

export interface ConformanceCluster {
  key: string;
  primaryCategory: string;
  eipCluster: string;
  count: number;
}

export interface ConformanceSnapshot {
  found: boolean;
  runId?: string;
  done: boolean;
  cancelled?: boolean;
  passed: number;
  failed: number;
  total: number;
  currentCase: string;
  status: string;
  failures: ConformanceFailure[];
  clusters: ConformanceCluster[];
}

export interface ConformancePrepare {
  valid: boolean;
  resolvedRoot: string;
  fileCount: number;
  forks: string[];
}

async function rpcCall(method: string, params: unknown[]): Promise<unknown> {
  const { endpoint } = useAppStore.getState();
  const response = await fetch(endpoint, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ jsonrpc: '2.0', id: Date.now(), method, params }),
  });
  if (!response.ok) throw new Error(`RPC HTTP ${response.status}`);
  const json = await response.json();
  if (json.error) throw new Error(`RPC Error ${json.error.code}: ${json.error.message}`);
  return json.result;
}

export function prepareConformance(input: {
  fork: string;
  fixturesBasePath?: string;
  fixturesRoot?: string;
  excludePortedStatic?: boolean;
}): Promise<ConformancePrepare> {
  return rpcCall('schlieren_conformancePrepare', [input]) as Promise<ConformancePrepare>;
}

export async function startConformance(input: {
  fork: string;
  fixturesBasePath?: string;
  fixturesRoot?: string;
  excludePortedStatic?: boolean;
  maxCases?: number;
}): Promise<string> {
  const result = await rpcCall('schlieren_conformanceStart', [input]) as { runId: string };
  return result.runId;
}

export function pollConformance(runId?: string): Promise<ConformanceSnapshot> {
  return rpcCall('schlieren_conformancePoll', [{ runId: runId ?? '' }]) as Promise<ConformanceSnapshot>;
}

export function cancelConformance(runId?: string): Promise<{ cancelled: boolean }> {
  return rpcCall('schlieren_conformanceCancel', [{ runId: runId ?? '' }]) as Promise<{ cancelled: boolean }>;
}

export function readConformanceFixture(path: string): Promise<{ path: string; name: string; text: string }> {
  return rpcCall('schlieren_conformanceReadFixture', [{ path }]) as Promise<{ path: string; name: string; text: string }>;
}
