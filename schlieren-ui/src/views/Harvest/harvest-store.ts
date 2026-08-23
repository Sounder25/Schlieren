import { create } from 'zustand';

export interface HarvestedContract {
  address: string;
  bytecode: string;
  sizeBytes: number;
  network: string;
  harvestedAt: number;
  label?: string;
}

interface HarvestState {
  targetAddress: string;
  setTargetAddress: (addr: string) => void;

  providerUrl: string;
  setProviderUrl: (url: string) => void;

  contracts: HarvestedContract[];
  addContract: (c: HarvestedContract) => void;
  removeContract: (address: string) => void;

  isHarvesting: boolean;
  setIsHarvesting: (v: boolean) => void;

  error: string | null;
  setError: (e: string | null) => void;
}

const STORAGE_KEY = 'schlieren-harvest';

function load(): HarvestedContract[] {
  try {
    const s = localStorage.getItem(STORAGE_KEY);
    return s ? JSON.parse(s) : [];
  } catch { return []; }
}

function persist(contracts: HarvestedContract[]) {
  try { localStorage.setItem(STORAGE_KEY, JSON.stringify(contracts)); } catch {}
}

export const useHarvestStore = create<HarvestState>((set, get) => ({
  targetAddress: '',
  setTargetAddress: (addr) => set({ targetAddress: addr }),

  providerUrl: 'https://eth.llamarpc.com',
  setProviderUrl: (url) => set({ providerUrl: url }),

  contracts: load(),
  addContract: (c) => {
    const next = [c, ...get().contracts.filter(x => x.address.toLowerCase() !== c.address.toLowerCase())];
    persist(next);
    set({ contracts: next });
  },
  removeContract: (address) => {
    const next = get().contracts.filter(x => x.address.toLowerCase() !== address.toLowerCase());
    persist(next);
    set({ contracts: next });
  },

  isHarvesting: false,
  setIsHarvesting: (v) => set({ isHarvesting: v }),

  error: null,
  setError: (e) => set({ error: e }),
}));

/** Fetch bytecode from a public node */
export async function harvest(address: string): Promise<HarvestedContract> {
  const { providerUrl, setIsHarvesting, setError, addContract } = useHarvestStore.getState();
  const addr = address.trim().toLowerCase();

  if (!/^0x[0-9a-f]{40}$/.test(addr)) throw new Error('Invalid address');

  setIsHarvesting(true);
  setError(null);

  try {
    const res = await fetch(providerUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'eth_getCode', params: [addr, 'latest'] }),
    });

    const json = await res.json();
    if (json.error) throw new Error(json.error.message);

    const code = json.result as string;
    if (!code || code === '0x') throw new Error('No bytecode — EOA or empty address');

    const contract: HarvestedContract = {
      address: addr,
      bytecode: code,
      sizeBytes: (code.length - 2) / 2,
      network: 'mainnet',
      harvestedAt: Date.now(),
    };

    addContract(contract);
    return contract;
  } catch (err: any) {
    setError(err?.message || 'Harvest failed');
    throw err;
  } finally {
    setIsHarvesting(false);
  }
}
