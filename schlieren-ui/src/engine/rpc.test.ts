import { beforeEach, describe, expect, it, vi } from 'vitest';
import { loadFixture } from './fixture-adapter';
import { cancelTrace, executeTrace, importCode, setOpSec } from './rpc';
import { useAppStore } from './store';

const journalResult = {
  ok: true,
  fork: 'Osaka',
  execution: { success: true, error: null, gasUsed: 21003, gasRefundCounter: 0, returnData: '0x' },
  events: [{ kind: 'opcodeGas', sequence: 2, frameId: 1, parentFrameId: null, semantics: 'exclusiveCharge', amount: 3, component: null, pc: 0, opcode: '0x60', opcodeName: 'PUSH1', data: {} }],
  frames: [{ id: 1, parentId: null, depth: 0, callType: 'Root', contractAddress: '0xaa', codeAddress: null, gasLimit: 79000, success: true, error: 'None', gasUsed: 3, gasRemaining: 78997 }],
  steps: [{ sequence: 2, frameId: 1, parentFrameId: null, depth: 0, pc: 0, opcode: '0x60', op: 'PUSH1', gasBefore: 79000, gasAfter: 78997, gasCost: 3, semantics: 'exclusiveCharge', output: '0x', stack: [], memory: [], storage: {} }],
  gasTree: { id: 'transaction', label: 'Transaction', frameId: null, semantics: 'observation', amount: 0, effect: 'none', totalGas: 21003, eventSequences: [0, 1, 2], children: [] },
  conservation: { derivedGas: 21003, settledGas: 21003, delta: '0', isConserved: true },
};

describe('executeTrace', () => {
  beforeEach(() => {
    useAppStore.setState({
      config: {
        bytecode: '0x6001',
        calldata: '',
        from: '0x0000000000000000000000000000000000000001',
        to: '0x00000000000000000000000000000000000000aa',
        value: '0x0',
        gasLimit: 100000,
        fork: 'Osaka',
      },
      result: null,
      loadedFixture: null,
    });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ jsonrpc: '2.0', id: 1, result: journalResult }),
    }));
  });

  it('uses one atomic journal request including ephemeral code', async () => {
    const result = await executeTrace();

    expect(fetch).toHaveBeenCalledTimes(1);
    const init = vi.mocked(fetch).mock.calls[0][1] as RequestInit;
    const body = JSON.parse(init.body as string);
    expect(body.method).toBe('schlieren_traceJournal');
    expect(body.params).toEqual([expect.objectContaining({
      code: '0x6001',
      disableStack: false,
      disableMemory: false,
      disableStorage: false,
    })]);
    expect(result.frames[0].id).toBe(1);
    expect(result.frameTree).toBeNull();
    expect(result.stateEffects).toEqual([]);
    expect(result.securityFindings).toEqual([]);
    expect(result.conservation.isConserved).toBe(true);
    expect(useAppStore.getState().result).toEqual(result);
  });

  it('cancels an in-flight journal request', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      return new Promise((_resolve, reject) => {
        init?.signal?.addEventListener('abort', () => {
          reject(new DOMException('Aborted', 'AbortError'));
        });
      });
    }));

    const pending = executeTrace();
    expect(cancelTrace()).toBe(true);
    await expect(pending).rejects.toMatchObject({ name: 'AbortError' });
    expect(useAppStore.getState().isRunning).toBe(false);
    expect(useAppStore.getState().lastError).toBe('Run cancelled');
  });

  it('uses LoadedFixture.request as the exclusive execution source', async () => {
    const loaded = loadFixture(`{
      "case": {
        "env": {},
        "pre": { "0x00000000000000000000000000000000000000aa": { "nonce": "0x00", "balance": "0x01", "code": "0x00", "storage": {} } },
        "transaction": {
          "sender": "0x0000000000000000000000000000000000000001",
          "to": "0x00000000000000000000000000000000000000aa",
          "gasLimit": ["0x186a0"],
          "value": ["0x00"],
          "data": ["0x"],
          "secretKey": "0x01"
        },
        "post": { "Osaka": [{ "indexes": { "data": 0, "gas": 0, "value": 0 }, "state": {} }] }
      }
    }`);
    useAppStore.getState().applyLoadedFixture(loaded);
    await executeTrace();
    const body = JSON.parse(vi.mocked(fetch).mock.calls[0][1]!.body as string);
    expect(body.params[0].transaction.to).toBe('0x00000000000000000000000000000000000000aa');
    expect(body.params[0].preState).toHaveLength(1);
    expect(body.params[0].preState[0].code).toBe('0x00');
    expect(body.params[0].expected).toBeUndefined();
    expect(body.params[0].from).toBeUndefined();
    expect(JSON.stringify(body)).not.toContain('secretKey');
    expect(useAppStore.getState().loadedFixture?.expected).toBeDefined();
  });
});

describe('OpSec RPC authority', () => {
  beforeEach(() => {
    useAppStore.setState({ opSecEnabled: false });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ jsonrpc: '2.0', id: 1, result: { enabled: true } }),
    }));
  });

  it('setOpSec asks the RPC process, not a local flag', async () => {
    await setOpSec(true);
    const init = vi.mocked(fetch).mock.calls[0][1] as RequestInit;
    const body = JSON.parse(init.body as string);
    expect(body.method).toBe('schlieren_opsecSet');
    expect(body.params).toEqual([{ enabled: true }]);
    expect(useAppStore.getState().opSecEnabled).toBe(true);
  });

  it('importCode goes through schlieren_importCode', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        jsonrpc: '2.0',
        id: 1,
        result: { address: '0x0000000000000000000000000000000000000001', code: '0x6000' },
      }),
    }));
    const result = await importCode(
      '0x0000000000000000000000000000000000000001',
      'https://eth.llamarpc.com',
    );
    const init = vi.mocked(fetch).mock.calls[0][1] as RequestInit;
    const body = JSON.parse(init.body as string);
    expect(body.method).toBe('schlieren_importCode');
    expect(body.params[0].provider).toBe('https://eth.llamarpc.com');
    expect(result.code).toBe('0x6000');
  });
});
