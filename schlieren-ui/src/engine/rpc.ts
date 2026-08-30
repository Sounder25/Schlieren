import { parseJournalTrace } from './journal';
import { useAppStore, type ExecutionResult } from './store';

let inflight: AbortController | null = null;
let runGeneration = 0;

async function rpcCall(method: string, params: unknown[], signal?: AbortSignal): Promise<unknown> {
  const { endpoint } = useAppStore.getState();
  const response = await fetch(endpoint, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ jsonrpc: '2.0', id: Date.now(), method, params }),
    signal,
  });
  if (!response.ok) throw new Error(`RPC HTTP ${response.status}: ${response.statusText}`);
  const json = await response.json();
  if (json.error) throw new Error(`RPC Error ${json.error.code}: ${json.error.message}`);
  return json.result;
}

export function cancelTrace(): boolean {
  if (!inflight) return false;
  inflight.abort();
  inflight = null;
  return true;
}

export async function executeTrace(): Promise<ExecutionResult> {
  const generation = ++runGeneration;
  inflight?.abort();
  const controller = new AbortController();
  inflight = controller;

  const { config, loadedFixture, setIsRunning, setResult, setLastError } = useAppStore.getState();
  setIsRunning(true);
  setLastError(null);
  try {
    const request = loadedFixture
      ? loadedFixture.request
      : flatRequestFromConfig(config);

    const journal = parseJournalTrace(await rpcCall('schlieren_traceJournal', [request], controller.signal));
    const result: ExecutionResult = {
      ...journal,
      success: journal.execution.success,
      gasUsed: journal.execution.gasUsed,
      returnData: journal.execution.returnData,
      error: journal.execution.error ?? undefined,
    };
    setResult(result);
    return result;
  } catch (err) {
    const aborted = controller.signal.aborted || (err instanceof DOMException && err.name === 'AbortError');
    if (aborted) {
      if (generation === runGeneration) setLastError('Run cancelled');
      throw err instanceof Error ? err : new DOMException('Aborted', 'AbortError');
    }
    const message = err instanceof Error ? err.message : String(err);
    setLastError(message);
    throw err;
  } finally {
    if (inflight === controller) inflight = null;
    if (generation === runGeneration) setIsRunning(false);
  }
}

function flatRequestFromConfig(config: ReturnType<typeof useAppStore.getState>['config']) {
  const request: Record<string, unknown> = {
    from: config.from,
    to: config.to,
    gas: `0x${config.gasLimit.toString(16)}`,
    value: config.value || '0x0',
    data: config.calldata
      ? (config.calldata.startsWith('0x') ? config.calldata : `0x${config.calldata}`)
      : '0x',
    fork: config.fork,
    disableStack: false,
    disableMemory: false,
    disableStorage: false,
  };
  if (config.bytecode) {
    request.code = config.bytecode.startsWith('0x')
      ? config.bytecode
      : `0x${config.bytecode}`;
  }
  return request;
}

export async function setOpSec(enabled: boolean): Promise<boolean> {
  const result = await rpcCall('schlieren_opsecSet', [{ enabled }]) as { enabled: boolean };
  useAppStore.getState().setOpSecEnabled(result.enabled);
  return result.enabled;
}

export async function checkOpSec(): Promise<boolean> {
  try {
    const result = await rpcCall('schlieren_opsecStatus', []) as { enabled: boolean };
    useAppStore.getState().setOpSecEnabled(result.enabled);
    return result.enabled;
  } catch {
    return false;
  }
}

export async function importCode(address: string, provider: string): Promise<{ address: string; code: string }> {
  return rpcCall('schlieren_importCode', [{ address, provider }]) as Promise<{ address: string; code: string }>;
}

export async function checkConnection(): Promise<boolean> {
  const { setConnected } = useAppStore.getState();
  try {
    const version = await rpcCall('web3_clientVersion', []);
    const connected = typeof version === 'string' && version.startsWith('Schlieren');
    setConnected(connected);
    return connected;
  } catch {
    setConnected(false);
    return false;
  }
}
