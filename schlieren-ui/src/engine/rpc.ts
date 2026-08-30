import { parseJournalTrace } from './journal';
import { useAppStore, type ExecutionResult, type RunConfig } from './store';

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

export async function executeTrace(): Promise<ExecutionResult> {
  const { config, guardReplay, setIsRunning, setResult } = useAppStore.getState();
  setIsRunning(true);
  try {
    const request = guardReplay?.params[0] ?? buildFlatRequest(config);

    const journal = parseJournalTrace(await rpcCall('schlieren_traceJournal', [request]));
    const result: ExecutionResult = {
      ...journal,
      success: journal.execution.success,
      gasUsed: journal.execution.gasUsed,
      returnData: journal.execution.returnData,
      error: journal.execution.error ?? undefined,
    };
    setResult(result);
    return result;
  } finally {
    setIsRunning(false);
  }
}

function buildFlatRequest(config: RunConfig): Record<string, unknown> {
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
