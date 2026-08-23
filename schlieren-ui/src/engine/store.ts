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
  severity: string;
  primaryFrameId: number;
  supportingEventSequences: number[];
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
  endpoint: string;
  setEndpoint: (url: string) => void;
  connected: boolean;
  setConnected: (connected: boolean) => void;
}

export const useAppStore = create<AppState>((set) => ({
  activeView: 'workbench',
  setActiveView: (activeView) => set({ activeView }),
  config: {
    bytecode: '',
    calldata: '',
    from: '0x0000000000000000000000000000000000000001',
    to: '0x00000000000000000000000000000000000000aa',
    value: '0x0',
    gasLimit: 10_000_000,
    fork: 'Osaka',
  },
  setConfig: (partial) => set((state) => ({ config: { ...state.config, ...partial } })),
  result: null,
  setResult: (result) => set({ result, currentStep: 0 }),
  isRunning: false,
  setIsRunning: (isRunning) => set({ isRunning }),
  currentStep: 0,
  setCurrentStep: (currentStep) => set({ currentStep }),
  endpoint: 'http://localhost:8545',
  setEndpoint: (endpoint) => set({ endpoint }),
  connected: false,
  setConnected: (connected) => set({ connected }),
}));
