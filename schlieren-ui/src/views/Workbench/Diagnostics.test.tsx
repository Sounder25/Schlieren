import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { ExecutionResult, JournalSecurityFinding } from '../../engine/store';
import { SecurityFindings } from './Diagnostics';

describe('Diagnostics', () => {
  it('renders server-provided security proof fields', () => {
    const html = renderToStaticMarkup(
      <SecurityFindings result={makeExecutionWithFinding()} onFocus={() => undefined} />,
    );

    expect(html).toContain('STATE_CONTACT');
    expect(html).toContain('MEDIUM');
    expect(html).toContain('Frame 7');
    expect(html).toContain('simulationDiscarded');
    expect(html).toContain('Observed path only');
  });

  it('shows a true empty state only when the server returned no findings', () => {
    const html = renderToStaticMarkup(
      <SecurityFindings result={makeExecutionWithoutFindings()} onFocus={() => undefined} />,
    );

    expect(html).toContain('No findings in this execution');
    expect(html).not.toContain('unchecked returns');
    expect(html).not.toContain('gas griefing');
  });
});

function makeExecutionWithFinding(): ExecutionResult {
  const finding: JournalSecurityFinding = {
    id: 'finding-1', ruleId: 'SEC.REENTRANCY.STATE_CONTACT', category: 'reentrancy', severity: 'medium',
    factGrade: 'proven', primaryFrameId: 7, primaryInstructionId: 99, supportingEventSequences: [12, 13],
    frameAncestry: [], executionDisposition: 'survived', persistenceDisposition: 'simulationDiscarded',
    addresses: ['0xaa'], storageSlots: ['0x0'], summary: 'Re-entry touched contract storage.',
    limitation: 'Observed path only.',
  };
  const frame = {
    id: 7, parentId: null, depth: 0, callType: 'CALL', contractAddress: '0xaa', codeAddress: '0xaa',
    gasLimit: 100, success: true, error: null, gasUsed: 3, gasRemaining: 97,
  };
  return {
    ok: true, fork: 'Osaka', success: true, error: undefined, gasUsed: 3,
    returnData: '0x', execution: { success: true, error: null, gasUsed: 3, gasRefundCounter: 0, returnData: '0x' },
    events: [{ kind: 'opcodeGas', sequence: 13, instructionId: 99, frameId: 7, parentFrameId: null,
      semantics: 'exclusive', amount: 3, component: 'opcode', pc: 0, opcode: '0x54', opcodeName: 'SLOAD', data: {} }],
    frames: [frame],
    steps: [{ sequence: 13, frameId: 7, parentFrameId: null, depth: 0, pc: 0, opcode: '0x54', op: 'SLOAD',
      gasBefore: 100, gasAfter: 97, gasCost: 3, semantics: 'exclusive', output: '0x' }],
    gasTree: { id: 'root', label: 'execution', frameId: 7, semantics: 'exclusive', amount: 3,
      effect: 'charge', totalGas: 3, eventSequences: [13], children: [] },
    conservation: { derivedGas: 3, settledGas: 3, delta: '0', isConserved: true },
    stateEffects: [], securityFindings: [finding],
    frameTree: { frame, ancestorIds: [], stateEffectIds: [], securityFindingIds: [finding.id], children: [] },
  };
}

function makeExecutionWithoutFindings(): ExecutionResult {
  const execution = makeExecutionWithFinding();
  return {
    ...execution,
    securityFindings: [],
    frameTree: execution.frameTree ? { ...execution.frameTree, securityFindingIds: [] } : null,
  };
}
