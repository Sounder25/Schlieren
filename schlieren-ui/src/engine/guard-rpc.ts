import { useAppStore, type GuardReport } from './store';

async function rpcCall(method: string, params: unknown[]): Promise<unknown> {
  const { endpoint } = useAppStore.getState();
  const response = await fetch(endpoint, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ jsonrpc: '2.0', id: Date.now(), method, params }),
  });
  if (!response.ok) throw new Error(`RPC HTTP ${response.status}: ${response.statusText}`);
  const json = await response.json();
  if (json.error) throw new Error(`RPC Error ${json.error.code}: ${json.error.message}`);
  return json.result;
}

export interface GuardRequest {
  token: string;
  rpc: string;
  block?: number | 'latest';
}

export async function executeGuard(req: GuardRequest): Promise<GuardReport> {
  const { setGuardRunning, setGuardReport, setGuardError } = useAppStore.getState();
  setGuardRunning(true);
  setGuardError(null);
  try {
    const result = await rpcCall('schlieren_guard', [req]);
    const report = result as GuardReport;
    setGuardReport(report);
    return report;
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    setGuardError(message);
    throw err;
  } finally {
    setGuardRunning(false);
  }
}
