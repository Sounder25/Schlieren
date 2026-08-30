import { describe, expect, it } from 'vitest';
import { buildAuditReport, buildTraceExport } from './export';
import { FORKS } from './forks';
import { DEFAULT_RUN_CONFIG, useAppStore, type ExecutionResult } from './store';

function sampleResult(): ExecutionResult {
  return {
    ok: true,
    fork: 'Osaka',
    success: true,
    gasUsed: 21003,
    returnData: '0x',
    execution: { success: true, error: null, gasUsed: 21003, gasRefundCounter: 0, returnData: '0x' },
    events: [],
    frames: [],
    steps: [{
      sequence: 1, frameId: 1, parentFrameId: null, depth: 0, pc: 0, opcode: '0x60', op: 'PUSH1',
      gasBefore: 100, gasAfter: 97, gasCost: 3, semantics: 'exclusiveCharge', output: '0x',
      stack: [], memory: [], storage: {},
    }],
    gasTree: { id: 'root', label: 'tx', frameId: null, semantics: 'observation', amount: 0, effect: 'none', totalGas: 21003, eventSequences: [], children: [] },
    conservation: { derivedGas: 21003, settledGas: 21003, delta: '0', isConserved: true },
    stateEffects: [],
    securityFindings: [],
    frameTree: null,
  };
}

describe('workbench export and reset', () => {
  it('emits RPC-canonical fork names', () => {
    expect(FORKS).toContain('TangerineWhistle');
    expect(FORKS).toContain('SpuriousDragon');
    expect(FORKS).not.toContain('Tangerine');
    expect(FORKS).not.toContain('Spurious');
  });

  it('builds a structLog trace payload from the journal result', () => {
    const payload = buildTraceExport(sampleResult(), DEFAULT_RUN_CONFIG);
    expect(payload.format).toBe('schlieren-structLog-v1');
    expect(payload.steps).toHaveLength(1);
    expect(payload.steps[0].op).toBe('PUSH1');
    expect(payload.conservation.isConserved).toBe(true);
  });

  it('builds a markdown audit report', () => {
    const md = buildAuditReport(sampleResult(), DEFAULT_RUN_CONFIG);
    expect(md).toContain('# SCHLIEREN');
    expect(md).toContain('Osaka');
    expect(md).toContain('No journal-backed security findings');
  });

  it('escapes pipes in finding cells', () => {
    const result = sampleResult();
    result.securityFindings = [{
      id: '1',
      ruleId: 'reentrancy',
      category: 'reentrancy',
      severity: 'high',
      factGrade: 'proven',
      primaryFrameId: 2,
      primaryInstructionId: null,
      supportingEventSequences: [],
      frameAncestry: [1, 2],
      executionDisposition: 'reverted',
      persistenceDisposition: 'simulationDiscarded',
      addresses: [],
      storageSlots: [],
      summary: 'Target: X | step Y',
      limitation: '',
    }];
    const md = buildAuditReport(result, DEFAULT_RUN_CONFIG);
    expect(md).toContain('Target: X \\| step Y');
  });

  it('resets bytecode, calldata, and result but keeps fork and addresses', () => {
    useAppStore.setState({
      config: {
        ...DEFAULT_RUN_CONFIG,
        bytecode: '0x6001',
        calldata: '0xdead',
        fork: 'Cancun',
        to: '0x00000000000000000000000000000000000000bb',
      },
      result: sampleResult(),
      currentStep: 4,
      lastError: 'boom',
    });
    useAppStore.getState().resetWorkbench();
    const state = useAppStore.getState();
    expect(state.result).toBeNull();
    expect(state.currentStep).toBe(0);
    expect(state.lastError).toBeNull();
    expect(state.config.bytecode).toBe('');
    expect(state.config.calldata).toBe('');
    expect(state.config.fork).toBe('Cancun');
    expect(state.config.to).toBe('0x00000000000000000000000000000000000000bb');
  });
});
