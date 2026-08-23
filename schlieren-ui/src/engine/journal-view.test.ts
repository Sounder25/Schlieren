import { describe, expect, it } from 'vitest';
import { buildFrameRows, buildSecurityRows, findSecurityFindingStepIndex, gasEffect, getConservationState } from './journal-view';
import type { JournalEvent, JournalFrameTreeNode, JournalSecurityFinding, TraceStep } from './store';

describe('journal view model', () => {
  it('keeps nested frames attached to their explicit parent', () => {
    const frameTree: JournalFrameTreeNode = {
      frame: { id: 1, parentId: null, depth: 0, callType: 'CALL', contractAddress: '0xaa', codeAddress: '0xaa', gasLimit: 100, success: true, error: null, gasUsed: 40, gasRemaining: 60 },
      ancestorIds: [], stateEffectIds: [2], securityFindingIds: [],
      children: [{
        frame: { id: 7, parentId: 1, depth: 1, callType: 'STATICCALL', contractAddress: '0xbb', codeAddress: '0xbb', gasLimit: 30, success: true, error: null, gasUsed: 9, gasRemaining: 21 },
        ancestorIds: [1], stateEffectIds: [8], securityFindingIds: ['finding-1'], children: [],
      }],
    };
    expect(buildFrameRows(frameTree)).toEqual([
      expect.objectContaining({ id: 1, parentId: null, indent: 0 }),
      expect.objectContaining({ id: 7, parentId: 1, indent: 1, ancestorIds: [1], stateEffectIds: [8] }),
    ]);
  });

  it('does not present gas evidence as an additive charge', () => {
    expect(gasEffect({ semantics: 'exclusive', amount: 3 })).toBe('charge');
    expect(gasEffect({ semantics: 'credit', amount: 2 })).toBe('credit');
    expect(gasEffect({ semantics: 'forwarded-allocation', amount: 50 })).toBe('evidence');
  });

  it('uses server-linked finding IDs and ancestry without rebuilding either', () => {
    const finding: JournalSecurityFinding = {
      id: 'SEC.REENTRANCY.REENTRY:12', ruleId: 'SEC.REENTRANCY.REENTRY',
      category: 'reentrancy', severity: 'medium', factGrade: 'proven',
      primaryFrameId: 7, primaryInstructionId: 9, supportingEventSequences: [12],
      frameAncestry: [1], executionDisposition: 'survived',
      persistenceDisposition: 'simulationDiscarded', addresses: ['0xaa'],
      storageSlots: ['0x0'], summary: 'Re-entry observed.', limitation: 'Observed path only.',
    };
    const tree: JournalFrameTreeNode = {
      frame: { id: 1, parentId: null, depth: 0, callType: 'ROOT', contractAddress: '0xaa', codeAddress: null, gasLimit: 100, success: true, error: null, gasUsed: 40, gasRemaining: 60 },
      ancestorIds: [], stateEffectIds: [], securityFindingIds: [], children: [{
        frame: { id: 7, parentId: 1, depth: 1, callType: 'CALL', contractAddress: '0xaa', codeAddress: '0xaa', gasLimit: 30, success: true, error: null, gasUsed: 9, gasRemaining: 21 },
        ancestorIds: [1], stateEffectIds: [3], securityFindingIds: [finding.id], children: [],
      }],
    };

    expect(buildSecurityRows(tree, [finding])).toEqual([
      expect.objectContaining({ id: finding.id, frameId: 7, framePath: [1, 7], severity: 'medium' }),
    ]);
  });

  it('makes a conservation failure impossible to miss', () => {
    expect(getConservationState({ derivedGas: 99, settledGas: 100, delta: '-1', isConserved: false }))
      .toEqual({ tone: 'fracture', label: 'DRIFT -1 GAS' });
  });

  it('navigates a finding through its server instruction link', () => {
    const finding = makeFinding({ primaryFrameId: 7, primaryInstructionId: 99 });
    const events = [
      makeEvent({ kind: 'storageRead', sequence: 12, instructionId: 99, frameId: 7 }),
      makeEvent({ kind: 'opcodeGas', sequence: 13, instructionId: 99, frameId: 7 }),
    ];
    const steps = [makeStep({ sequence: 13, frameId: 7 })];

    expect(findSecurityFindingStepIndex(finding, events, steps)).toBe(0);
  });

  it('falls back to the first step in the server primary frame', () => {
    const finding = makeFinding({ primaryFrameId: 7, primaryInstructionId: null });
    const steps = [makeStep({ sequence: 4, frameId: 1 }), makeStep({ sequence: 8, frameId: 7 })];

    expect(findSecurityFindingStepIndex(finding, [], steps)).toBe(1);
  });
});

function makeFinding(override: Partial<JournalSecurityFinding> = {}): JournalSecurityFinding {
  return {
    id: 'finding-1', ruleId: 'SEC.REENTRANCY.STATE_CONTACT', category: 'reentrancy', severity: 'medium',
    factGrade: 'proven', primaryFrameId: 1, primaryInstructionId: null, supportingEventSequences: [],
    frameAncestry: [], executionDisposition: 'survived', persistenceDisposition: 'simulationDiscarded',
    addresses: [], storageSlots: [], summary: 'Re-entry observed.', limitation: 'Observed path only.',
    ...override,
  };
}

function makeEvent(override: Partial<JournalEvent> = {}): JournalEvent {
  return {
    kind: 'opcodeGas', sequence: 1, instructionId: 1, frameId: 1, parentFrameId: null,
    semantics: 'exclusive', amount: 3, component: 'opcode', pc: 0, opcode: '0x00',
    opcodeName: 'STOP', data: {}, ...override,
  };
}

function makeStep(override: Partial<TraceStep> = {}): TraceStep {
  return {
    sequence: 1, frameId: 1, parentFrameId: null, depth: 0, pc: 0, opcode: '0x00', op: 'STOP',
    gasBefore: 100, gasAfter: 100, gasCost: 0, semantics: 'exclusive', output: '0x', ...override,
  };
}
