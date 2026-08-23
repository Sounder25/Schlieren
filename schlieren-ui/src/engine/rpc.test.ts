import { beforeEach, describe, expect, it, vi } from 'vitest';
import { executeTrace } from './rpc';
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
});
