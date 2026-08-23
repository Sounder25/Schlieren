import { describe, expect, it } from 'vitest';
import { buildFrameRows, gasEffect, getConservationState } from './journal-view';
import type { JournalFrameTreeNode } from './store';

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

  it('makes a conservation failure impossible to miss', () => {
    expect(getConservationState({ derivedGas: 99, settledGas: 100, delta: '-1', isConserved: false }))
      .toEqual({ tone: 'fracture', label: 'DRIFT -1 GAS' });
  });
});
