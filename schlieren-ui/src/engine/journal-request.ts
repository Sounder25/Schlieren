/** Normalized schlieren_traceJournal request. No expected post-state. */

export interface TraceAccessListEntry {
  address: string;
  storageKeys: string[];
}

export interface TraceAuthorization {
  chainId: string;
  address: string;
  nonce: string;
  signer?: string;
  valid: boolean;
}

export interface TraceTransaction {
  type?: string;
  from: string;
  to?: string | null;
  nonce?: string;
  gasLimit: string;
  gasPrice?: string;
  maxFeePerGas?: string;
  maxPriorityFeePerGas?: string;
  maxFeePerBlobGas?: string;
  value: string;
  data: string;
  accessList?: TraceAccessListEntry[];
  authorizationList?: TraceAuthorization[];
  blobVersionedHashes?: string[];
}

export interface TracePreStateAccount {
  address: string;
  nonce: string;
  balance: string;
  code: string;
  storage: Record<string, string>;
}

export interface TraceBlockContext {
  number?: string;
  timestamp?: string;
  coinbase?: string;
  gasLimit?: string;
  baseFee?: string;
  chainId?: string;
  prevRandao?: string;
  excessBlobGas?: string;
}

export interface TraceJournalRequest {
  fork: string;
  transaction: TraceTransaction;
  preState: TracePreStateAccount[];
  blockContext?: TraceBlockContext;
  /** Ephemeral overlay at transaction.to. Omitted when code lives in preState. */
  code?: string;
  options?: {
    disableStack?: boolean;
    disableMemory?: boolean;
    disableStorage?: boolean;
  };
}

export interface ExpectedAccount {
  address: string;
  nonce: string;
  balance: string;
  code: string;
  storage: Record<string, string>;
}

export interface FixtureExpected {
  success: boolean;
  exception: string | null;
  postHash: string | null;
  postState: ExpectedAccount[];
}

export interface FixtureIdentity {
  path: string;
  name: string;
  caseId: string;
  fork: string;
  kind: 'state-test' | 'prestate' | 'hex';
}

export interface LoadedFixture {
  identity: FixtureIdentity;
  /** Original file text. Not sent to RPC. */
  source: string;
  /** Drives schlieren_traceJournal. Contains no expected-result fields. */
  request: TraceJournalRequest;
  expected: FixtureExpected;
}
