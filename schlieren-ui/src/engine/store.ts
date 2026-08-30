import { create } from 'zustand';

export type ViewId = 'workbench' | 'interference' | 'flow' | 'conformance' | 'harvest';

export interface JournalEvent {
  kind: string;
  sequence: number;
  instructionId: number | null;
  frameId: number | null;
  parentFrameId: number | null;
  semantics: string;
  amount: number | null;
  component: string | null;
  pc: number | null;
  opcode: string | null;
  opcodeName: string | null;
  data: Record<string, unknown>;
}

export interface JournalFrame {
  id: number;
  parentId: number | null;
  depth: number;
  callType: string;
  contractAddress: string;
  codeAddress: string | null;
  gasLimit: number;
  success: boolean | null;
  error: string | null;
  gasUsed: number | null;
  gasRemaining: number | null;
}

export interface JournalStateEffect {
  effectId: number;
  sequence: number;
  frameId: number | null;
  parentFrameId: number | null;
  instructionId: number | null;
  kind: string;
  pc: number | null;
  opcode: string | null;
  executionDisposition: 'survived' | 'reverted';
  persistenceDisposition: 'committedToState' | 'simulationDiscarded' | 'notApplicable';
  revertedByFrameId: number | null;
  data: Record<string, unknown>;
}

export interface JournalSecurityFinding {
  id: string;
  ruleId: string;
  category: 'reentrancy' | 'storageCollision';
  severity: string;
  factGrade: string;
  primaryFrameId: number;
  primaryInstructionId: number | null;
  supportingEventSequences: number[];
  frameAncestry: number[];
  executionDisposition: 'survived' | 'reverted';
  persistenceDisposition: 'committedToState' | 'simulationDiscarded' | 'notApplicable';
  addresses: string[];
  storageSlots: string[];
  summary: string;
  limitation: string;
}

export interface JournalFrameTreeNode {
  frame: JournalFrame;
  ancestorIds: number[];
  stateEffectIds: number[];
  securityFindingIds: string[];
  children: JournalFrameTreeNode[];
}

export interface TraceStep {
  sequence: number;
  frameId: number;
  parentFrameId: number | null;
  depth: number;
  pc: number;
  opcode: string;
  op: string;
  gasBefore: number;
  gasAfter: number;
  gasCost: number;
  semantics: string;
  callType?: string | null;
  contractAddress?: string | null;
  callerAddress?: string | null;
  codeAddress?: string | null;
  output: string;
  stack?: string[];
  memory?: string[];
  storage?: Record<string, string>;
}

export interface JournalGasNode {
  id: string;
  label: string;
  frameId: number | null;
  semantics: string;
  amount: number;
  effect: 'none' | 'charge' | 'credit';
  totalGas: number;
  eventSequences: number[];
  children: JournalGasNode[];
}

export interface JournalConservation {
  derivedGas: number;
  settledGas: number;
  delta: string;
  isConserved: boolean;
}

export interface JournalExecution {
  success: boolean;
  error: string | null;
  gasUsed: number;
  gasRefundCounter: number;
  returnData: string;
}

export interface JournalTraceResponse {
  ok: boolean;
  fork: string;
  execution: JournalExecution;
  events: JournalEvent[];
  frames: JournalFrame[];
  steps: TraceStep[];
  gasTree: JournalGasNode;
  conservation: JournalConservation;
  stateEffects: JournalStateEffect[];
  securityFindings: JournalSecurityFinding[];
  frameTree: JournalFrameTreeNode | null;
}

export interface ExecutionResult extends JournalTraceResponse {
  success: boolean;
  gasUsed: number;
  returnData: string;
  error?: string;
}

export interface RunConfig {
  bytecode: string;
  calldata: string;
  from: string;
  to: string;
  value: string;
  gasLimit: number;
  fork: string;
}

export const DEFAULT_RUN_CONFIG: RunConfig = {
  bytecode: '',
  calldata: '',
  from: '0x0000000000000000000000000000000000000001',
  to: '0x00000000000000000000000000000000000000aa',
  value: '0x0',
  gasLimit: 10_000_000,
  fork: 'Osaka',
};

interface AppState {
  activeView: ViewId;
  setActiveView: (view: ViewId) => void;
  config: RunConfig;
  setConfig: (partial: Partial<RunConfig>) => void;
  result: ExecutionResult | null;
  setResult: (result: ExecutionResult | null) => void;
  isRunning: boolean;
  setIsRunning: (running: boolean) => void;
  currentStep: number;
  setCurrentStep: (step: number) => void;
  selectedFrameId: number | null;
  setSelectedFrameId: (id: number | null) => void;
  lastError: string | null;
  setLastError: (error: string | null) => void;
  endpoint: string;
  setEndpoint: (url: string) => void;
  connected: boolean;
  setConnected: (connected: boolean) => void;
  opSecEnabled: boolean;
  setOpSecEnabled: (enabled: boolean) => void;
  loadedFixture: import('./journal-request').LoadedFixture | null;
  setLoadedFixture: (fixture: import('./journal-request').LoadedFixture | null) => void;
  applyLoadedFixture: (fixture: import('./journal-request').LoadedFixture) => void;
  resetWorkbench: () => void;
}

export const useAppStore = create<AppState>((set) => ({
  activeView: 'workbench',
  setActiveView: (activeView) => set({ activeView }),
  config: { ...DEFAULT_RUN_CONFIG },
  setConfig: (partial) => set((state) => ({ config: { ...state.config, ...partial } })),
  result: null,
  setResult: (result) => set({ result, currentStep: 0, selectedFrameId: null, lastError: null }),
  isRunning: false,
  setIsRunning: (isRunning) => set({ isRunning }),
  currentStep: 0,
  setCurrentStep: (currentStep) => set({ currentStep }),
  selectedFrameId: null,
  setSelectedFrameId: (selectedFrameId) => set({ selectedFrameId }),
  lastError: null,
  setLastError: (lastError) => set({ lastError }),
  endpoint: 'http://localhost:8545',
  setEndpoint: (endpoint) => set({ endpoint }),
  connected: false,
  setConnected: (connected) => set({ connected }),
  opSecEnabled: false,
  setOpSecEnabled: (opSecEnabled) => set({ opSecEnabled }),
  loadedFixture: null,
  setLoadedFixture: (loadedFixture) => set({ loadedFixture }),
  applyLoadedFixture: (fixture) =>
    set((state) => ({
      loadedFixture: fixture,
      lastError: null,
      config: {
        ...state.config,
        fork: fixture.request.fork,
        from: fixture.request.transaction.from,
        to: fixture.request.transaction.to ?? '',
        calldata: fixture.request.transaction.data,
        value: fixture.request.transaction.value,
        gasLimit: Number.parseInt(fixture.request.transaction.gasLimit, 16) || state.config.gasLimit,
        bytecode: fixture.request.code ?? '',
      },
    })),
  resetWorkbench: () =>
    set((state) => ({
      result: null,
      currentStep: 0,
      selectedFrameId: null,
      lastError: null,
      loadedFixture: null,
      config: {
        ...state.config,
        bytecode: '',
        calldata: '',
      },
    })),
}));
