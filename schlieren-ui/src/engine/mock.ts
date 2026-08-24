import { useAppStore } from '../engine/store';
import type { JournalResult, JournalFrameNode } from '../engine/store';

// ─── Mock frame tree ──────────────────────────────────────────────────────────
// Simulates a reentrancy attack pattern:
// Root CALL → pool → CALL → attacker.receive → CALL (reentry) → pool → SSTORE

const mockTree: JournalFrameNode = {
  frameId: 1,
  parentFrameId: null,
  callType: 'Call',
  contractAddress: '0xAttacker1234567890abcdef1234567890abcdef',
  codeAddress:     '0xAttacker1234567890abcdef1234567890abcdef',
  executionDisposition: 'Survived',
  persistenceDisposition: 'SimulationDiscarded',
  ancestorIds: [],
  stateEffectIds: [1, 2],
  securityFindingIds: [],
  children: [
    {
      frameId: 2,
      parentFrameId: 1,
      callType: 'Call',
      contractAddress: '0xPool000000000000000000000000000000000001',
      codeAddress:     '0xPool000000000000000000000000000000000001',
      executionDisposition: 'Survived',
      persistenceDisposition: 'SimulationDiscarded',
      ancestorIds: [1],
      stateEffectIds: [3, 4, 5],
      securityFindingIds: ['finding-1'],
      children: [
        {
          frameId: 3,
          parentFrameId: 2,
          callType: 'Call',
          contractAddress: '0xAttacker1234567890abcdef1234567890abcdef',
          codeAddress:     '0xAttacker1234567890abcdef1234567890abcdef',
          executionDisposition: 'Survived',
          persistenceDisposition: 'SimulationDiscarded',
          ancestorIds: [1, 2],
          stateEffectIds: [6],
          securityFindingIds: ['finding-1'],
          children: [
            {
              frameId: 4,
              parentFrameId: 3,
              callType: 'Call',
              contractAddress: '0xPool000000000000000000000000000000000001',
              codeAddress:     '0xPool000000000000000000000000000000000001',
              executionDisposition: 'Survived',
              persistenceDisposition: 'SimulationDiscarded',
              ancestorIds: [1, 2, 3],
              stateEffectIds: [7, 8],
              securityFindingIds: ['finding-1'],
              children: [],
            },
          ],
        },
        {
          frameId: 5,
          parentFrameId: 2,
          callType: 'DelegateCall',
          contractAddress: '0xProxyAA0000000000000000000000000000000002',
          codeAddress:     '0xImplBB0000000000000000000000000000000003',
          executionDisposition: 'Survived',
          persistenceDisposition: 'SimulationDiscarded',
          ancestorIds: [1, 2],
          stateEffectIds: [9, 10],
          securityFindingIds: ['finding-2'],
          children: [],
        },
        {
          frameId: 6,
          parentFrameId: 2,
          callType: 'StaticCall',
          contractAddress: '0xOracle00000000000000000000000000000000aa',
          codeAddress:     '0xOracle00000000000000000000000000000000aa',
          executionDisposition: 'Reverted',
          persistenceDisposition: 'NotApplicable',
          revertedByFrameId: 6,
          ancestorIds: [1, 2],
          stateEffectIds: [],
          securityFindingIds: [],
          children: [],
        },
      ],
    },
  ],
};

const mockResult: JournalResult = {
  steps: [],
  gasUsed: 84231,
  success: true,
  returnData: '0x',
  fork: 'Osaka',
  frameTree: mockTree,
  stateEffects: [
    { sequence: 3, frameId: 2, kind: 'StorageWrite', slot: '0x01', value: '0x0a', originalValue: '0x00', previousValue: '0x00', storageAddress: '0xPool000000000000000000000000000000000001', executionDisposition: 'Survived', persistenceDisposition: 'SimulationDiscarded' },
    { sequence: 4, frameId: 2, kind: 'BalanceTransfer', sender: '0xPool000000000000000000000000000000000001', recipient: '0xAttacker1234567890abcdef1234567890abcdef', amount: '0x2386f26fc10000', reason: 'CallValue', executionDisposition: 'Survived', persistenceDisposition: 'SimulationDiscarded' },
    { sequence: 9, frameId: 5, kind: 'StorageWrite', slot: '0x360894a13ba1a3210667c828492db98dca3e2076cc3735a920a3ca505d382bbc', value: '0xImplBB0000000000000000000000000000000003', originalValue: '0x00', previousValue: '0x00', storageAddress: '0xProxyAA0000000000000000000000000000000002', executionDisposition: 'Survived', persistenceDisposition: 'SimulationDiscarded' },
  ],
  securityFindings: [
    {
      findingId: 'finding-1',
      ruleId: 'REENTRANCE_001',
      category: 'Reentrancy',
      severity: 'High',
      executionFactGrade: 'Confirmed',
      summary: 'Frame 3 re-entered pool storage context already active in frame 2. Write to balance slot occurred before update.',
      primaryFrameId: 3,
      primaryInstructionId: 48,
      supportingEventSequences: [3, 6, 7],
      frameAncestry: [1, 2, 3],
      executionDisposition: 'Survived',
      persistenceDisposition: 'SimulationDiscarded',
      affectedAddresses: ['0xPool000000000000000000000000000000000001'],
      affectedSlots: ['0x01'],
      proofLimitations: 'Proves re-entry occurred in this execution. Does not prove exploitability for all inputs.',
    },
    {
      findingId: 'finding-2',
      ruleId: 'STORAGE_COLLISION_001',
      category: 'StorageCollision',
      severity: 'High',
      executionFactGrade: 'Confirmed',
      summary: 'DELEGATECALL frame 5 wrote to EIP-1967 implementation slot (0x3608…) in proxy storage while executing implementation code.',
      primaryFrameId: 5,
      primaryInstructionId: 91,
      supportingEventSequences: [9],
      frameAncestry: [1, 2, 5],
      executionDisposition: 'Survived',
      persistenceDisposition: 'SimulationDiscarded',
      affectedAddresses: ['0xProxyAA0000000000000000000000000000000002'],
      affectedSlots: ['0x360894a13ba1a3210667c828492db98dca3e2076cc3735a920a3ca505d382bbc'],
      proofLimitations: 'Proves write to reserved EIP-1967 slot occurred and survived. Storage layout audit recommended.',
    },
  ],
  effectBySequence: new Map(),
  findingById: new Map(),
};

// Build indexes
mockResult.stateEffects.forEach((e) => mockResult.effectBySequence.set(e.sequence, e));
mockResult.securityFindings.forEach((f) => mockResult.findingById.set(f.findingId, f));

export function injectMockResult() {
  useAppStore.getState().setResult(mockResult);
}
