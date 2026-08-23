import { describe, expect, it } from 'vitest';
import { alignEelsTrace } from './eels';
import type { TraceStep } from './store';

const step: TraceStep = { sequence: 1, frameId: 4, parentFrameId: 1, depth: 1, pc: 0, opcode: '0x60', op: 'PUSH1', gasBefore: 100, gasAfter: 97, gasCost: 3, semantics: 'exclusiveCharge', output: '0x', stack: [], memory: [], storage: {} };

describe('EELS alignment', () => {
  it('accepts EIP-3155 structLogs and reports alignment', () => {
    expect(alignEelsTrace([step], JSON.stringify({ structLogs: [{ pc: 0, op: 'PUSH1', gas: 100, gasCost: 3, depth: 1 }] }))).toEqual({ isAligned: true, comparedSteps: 1, firstDivergence: null });
  });

  it('anchors first divergence to the journal frame', () => {
    const result = alignEelsTrace([step], JSON.stringify([{ pc: 0, op: 'PUSH1', gas: 100, gasCost: 4, depth: 1 }]));
    expect(result.firstDivergence).toEqual(expect.objectContaining({ index: 0, field: 'gasCost', expected: '4', actual: '3', frameId: 4 }));
  });
});
