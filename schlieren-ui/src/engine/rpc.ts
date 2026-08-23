import { useAppStore, type TraceStep, type ExecutionResult } from './store';

/**
 * Schlieren RPC client — talks to the Schlieren JSON-RPC backend.
 * Uses debug_traceCall to execute bytecode and get a full step trace.
 */

async function rpcCall(method: string, params: unknown[]): Promise<unknown> {
  const { endpoint } = useAppStore.getState();
  
  const response = await fetch(endpoint, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      jsonrpc: '2.0',
      id: Date.now(),
      method,
      params,
    }),
  });

  if (!response.ok) {
    throw new Error(`RPC HTTP ${response.status}: ${response.statusText}`);
  }

  const json = await response.json();
  if (json.error) {
    throw new Error(`RPC Error ${json.error.code}: ${json.error.message}`);
  }

  return json.result;
}

/**
 * Execute bytecode via debug_traceCall and return a full execution trace.
 */
export async function executeTrace(): Promise<ExecutionResult> {
  const { config, setIsRunning, setResult } = useAppStore.getState();
  
  setIsRunning(true);
  
  try {
    const callObj: Record<string, string> = {
      from: config.from,
      to: config.to,
      gas: '0x' + config.gasLimit.toString(16),
      value: config.value || '0x0',
    };
    
    if (config.calldata) {
      callObj.data = config.calldata.startsWith('0x') ? config.calldata : '0x' + config.calldata;
    }
    
    // If we have bytecode but need to deploy it to the target address first,
    // use anvil_setCode then trace
    if (config.bytecode) {
      const code = config.bytecode.startsWith('0x') ? config.bytecode : '0x' + config.bytecode;
      await rpcCall('anvil_setCode', [config.to, code]);
      if (!config.calldata) {
        callObj.data = '0x'; // empty calldata for pure bytecode execution
      }
    }

    const traceResult = await rpcCall('debug_traceCall', [
      callObj,
      'latest',
      { enableMemory: true, enableReturnData: true, disableStorage: false }
    ]) as {
      gas: number;
      failed: boolean;
      returnValue: string;
      structLogs: Array<{
        pc: number;
        op: string;
        gas: number;
        gasCost: number;
        depth: number;
        stack: string[];
        memory: string[];
        storage: Record<string, string>;
        error?: string;
      }>;
    };

    const steps: TraceStep[] = (traceResult.structLogs || []).map((log) => ({
      pc: log.pc,
      op: log.op,
      opHex: '', // we'll derive this from the bytecode
      gas: log.gas,
      gasCost: log.gasCost,
      depth: log.depth,
      stack: log.stack || [],
      memory: (log.memory || []).join(''),
      storage: log.storage || {},
    }));

    const result: ExecutionResult = {
      steps,
      gasUsed: traceResult.gas,
      success: !traceResult.failed,
      returnData: traceResult.returnValue ? '0x' + traceResult.returnValue : '0x',
      fork: config.fork,
    };

    setResult(result);
    return result;
  } finally {
    setIsRunning(false);
  }
}

/**
 * Check if the RPC endpoint is reachable.
 */
export async function checkConnection(): Promise<boolean> {
  const { setConnected } = useAppStore.getState();
  try {
    const version = await rpcCall('web3_clientVersion', []) as string;
    const isSchlieren = typeof version === 'string' && version.startsWith('Schlieren');
    setConnected(isSchlieren);
    return isSchlieren;
  } catch {
    setConnected(false);
    return false;
  }
}
