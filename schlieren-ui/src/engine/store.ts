import { create } from 'zustand';

// ─── Types ───────────────────────────────────────────────────────────────────

export type ViewId = 'workbench' | 'interference' | 'flow' | 'conformance' | 'harvest';

export interface TraceStep {
  pc: number;
  op: string;
  opHex: string;
  gas: number;
  gasCost: number;
  depth: number;
  stack: string[];
  memory: string;
  storage: Record<string, string>;
  returnData?: string;
  error?: string;
}

export interface ExecutionResult {
  steps: TraceStep[];
  gasUsed: number;
  success: boolean;
  returnData: string;
  error?: string;
  fork: string;
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

// ─── Store ───────────────────────────────────────────────────────────────────

interface AppState {
  // Navigation
  activeView: ViewId;
  setActiveView: (view: ViewId) => void;

  // Execution
  config: RunConfig;
  setConfig: (partial: Partial<RunConfig>) => void;
  
  result: ExecutionResult | null;
  setResult: (result: ExecutionResult | null) => void;
  
  isRunning: boolean;
  setIsRunning: (running: boolean) => void;

  // Step cursor
  currentStep: number;
  setCurrentStep: (step: number) => void;

  // Connection
  endpoint: string;
  setEndpoint: (url: string) => void;
  connected: boolean;
  setConnected: (c: boolean) => void;
}

export const useAppStore = create<AppState>((set) => ({
  // Navigation
  activeView: 'workbench',
  setActiveView: (view) => set({ activeView: view }),

  // Execution
  config: {
    bytecode: '',
    calldata: '',
    from: '0x0000000000000000000000000000000000000001',
    to: '0x00000000000000000000000000000000000000aa',
    value: '0x0',
    gasLimit: 10_000_000,
    fork: 'Osaka',
  },
  setConfig: (partial) => set((s) => ({ config: { ...s.config, ...partial } })),

  result: null,
  setResult: (result) => set({ result, currentStep: 0 }),

  isRunning: false,
  setIsRunning: (running) => set({ isRunning: running }),

  // Step cursor
  currentStep: 0,
  setCurrentStep: (step) => set({ currentStep: step }),

  // Connection
  endpoint: 'http://localhost:8545',
  setEndpoint: (url) => set({ endpoint: url }),
  connected: false,
  setConnected: (c) => set({ connected: c }),
}));
