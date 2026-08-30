import type {
  ExpectedAccount,
  FixtureExpected,
  LoadedFixture,
  TraceAuthorization,
  TraceBlockContext,
  TraceJournalRequest,
  TracePreStateAccount,
  TraceTransaction,
} from './journal-request';

export class FixtureAdapterError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'FixtureAdapterError';
  }
}

export interface LoadFixtureOptions {
  path?: string;
  preferredFork?: string;
  caseId?: string;
}

export function loadFixture(raw: string, options: LoadFixtureOptions = {}): LoadedFixture {
  const text = raw.replace(/^\uFEFF/, '');
  if (looksLikeStateTest(text))
    return adaptStateTest(text, options);
  if (looksLikePrestate(text))
    return adaptPrestate(text, options);
  if (looksLikeHex(text))
    return adaptHex(text, options);
  throw new FixtureAdapterError('Not a state_test, pre-state JSON, or hex bytecode.');
}

export function looksLikeStateTest(text: string): boolean {
  const t = text.trimStart();
  if (!t.startsWith('{')) return false;
  try {
    const parsed = JSON.parse(text) as unknown;
    if (!isRecord(parsed)) return false;
    if (isCaseNode(parsed)) return true;
    return Object.values(parsed).some((value) => isRecord(value) && isCaseNode(value));
  } catch {
    return false;
  }
}

export function looksLikePrestate(text: string): boolean {
  try {
    const parsed = JSON.parse(text) as unknown;
    if (Array.isArray(parsed)) return parsed.every((row) => isRecord(row) && typeof row.address === 'string');
    return isRecord(parsed) && Array.isArray(parsed.accounts);
  } catch {
    return false;
  }
}

export function looksLikeHex(text: string): boolean {
  const c = text.replace(/0x/gi, '').replace(/\s+/g, '');
  if (c.length < 4 || c.length % 2 !== 0) return false;
  return [...c].every((ch) => /[0-9a-f]/i.test(ch));
}

function adaptStateTest(raw: string, options: LoadFixtureOptions): LoadedFixture {
  const root = JSON.parse(raw) as Record<string, unknown>;
  const { name, node } = pickCase(root, options.caseId);
  if (!isRecord(node.pre) || !isRecord(node.transaction))
    throw new FixtureAdapterError(`${name}: missing pre or transaction.`);

  const post = isRecord(node.post) ? node.post : null;
  const { fork, variant } = pickPostVariant(post, options.preferredFork);
  const indexes = isRecord(variant?.indexes) ? variant.indexes : {};
  const dataIndex = asIndex(indexes.data);
  const gasIndex = asIndex(indexes.gas);
  const valueIndex = asIndex(indexes.value);

  const txNode = node.transaction;
  const sender = str(txNode.sender)
    ?? firstPreAddress(node.pre)
    ?? '0x0000000000000000000000000000000000000001';
  const toRaw = str(txNode.to);
  const to = !toRaw || toRaw === '0x' || toRaw === '0x0' ? null : toRaw;

  const data = variantBytes(txNode.data, dataIndex);
  const gasLimit = variantQty(txNode.gasLimit, gasIndex) ?? '0x989680';
  const value = variantQty(txNode.value, valueIndex) ?? '0x0';
  const nonce = qty(txNode.nonce);
  const gasPrice = optionalQty(txNode.gasPrice);
  const maxFeePerGas = optionalQty(txNode.maxFeePerGas);
  const maxPriorityFeePerGas = optionalQty(txNode.maxPriorityFeePerGas);
  const maxFeePerBlobGas = optionalQty(txNode.maxFeePerBlobGas);

  const accessList = variantAccessList(txNode, dataIndex);
  const authorizationList = adaptAuthorizations(txNode.authorizationList);
  const blobVersionedHashes = asStringArray(txNode.blobVersionedHashes);

  const transaction: TraceTransaction = {
    from: sender,
    to,
    gasLimit,
    value,
    data,
  };
  if (nonce !== undefined) transaction.nonce = nonce;
  if (gasPrice !== undefined) transaction.gasPrice = gasPrice;
  if (maxFeePerGas !== undefined) transaction.maxFeePerGas = maxFeePerGas;
  if (maxPriorityFeePerGas !== undefined) transaction.maxPriorityFeePerGas = maxPriorityFeePerGas;
  if (maxFeePerBlobGas !== undefined) transaction.maxFeePerBlobGas = maxFeePerBlobGas;
  if (accessList) transaction.accessList = accessList;
  if (authorizationList) transaction.authorizationList = authorizationList;
  if (blobVersionedHashes) transaction.blobVersionedHashes = blobVersionedHashes;
  if (authorizationList?.length) transaction.type = '0x4';
  else if (blobVersionedHashes?.length) transaction.type = '0x3';
  else if (maxFeePerGas !== undefined || maxPriorityFeePerGas !== undefined) transaction.type = '0x2';
  else if (accessList?.length) transaction.type = '0x1';

  const env = isRecord(node.env) ? node.env : {};
  const config = isRecord(node.config) ? node.config : {};
  const blockContext: TraceBlockContext = {
    coinbase: str(env.currentCoinbase),
    gasLimit: optionalQty(env.currentGasLimit),
    number: optionalQty(env.currentNumber),
    timestamp: optionalQty(env.currentTimestamp),
    baseFee: optionalQty(env.currentBaseFee),
    prevRandao: optionalQty(env.currentRandom) ?? optionalQty(env.currentDifficulty),
    excessBlobGas: optionalQty(env.currentExcessBlobGas),
    chainId: optionalQty(config.chainid) ?? optionalQty(config.chainId) ?? '0x1',
  };

  const request: TraceJournalRequest = {
    fork,
    transaction,
    preState: readPreAccounts(node.pre),
    blockContext,
    options: { disableStack: false, disableMemory: false, disableStorage: false },
  };

  assertNoForbiddenKeys(request);

  return {
    identity: {
      path: options.path ?? '',
      name: fileName(options.path) || name,
      caseId: name,
      fork,
      kind: 'state-test',
    },
    source: raw,
    request,
    expected: readExpected(variant),
  };
}

function adaptPrestate(raw: string, options: LoadFixtureOptions): LoadedFixture {
  const parsed = JSON.parse(raw) as unknown;
  const rows = Array.isArray(parsed)
    ? parsed
    : isRecord(parsed) && Array.isArray(parsed.accounts)
      ? parsed.accounts
      : [];
  const preState: TracePreStateAccount[] = rows.map((row, i) => {
    if (!isRecord(row) || typeof row.address !== 'string')
      throw new FixtureAdapterError(`Account #${i + 1} is missing address.`);
    return {
      address: row.address,
      nonce: optionalQty(row.nonce) ?? '0x0',
      balance: optionalQty(row.balance) ?? optionalQty(row.balanceWei) ?? '0x0',
      code: asHexBytes(str(row.code) ?? str(row.bytecode) ?? '0x'),
      storage: asStorage(row.storage),
    };
  });
  if (preState.length === 0)
    throw new FixtureAdapterError('No accounts in pre-state JSON.');

  const to = preState[0]?.address ?? null;
  const request: TraceJournalRequest = {
    fork: options.preferredFork ?? 'Osaka',
    transaction: {
      from: '0x0000000000000000000000000000000000000001',
      to,
      gasLimit: '0x989680',
      value: '0x0',
      data: '0x',
    },
    preState,
  };
  assertNoForbiddenKeys(request);
  return {
    identity: {
      path: options.path ?? '',
      name: fileName(options.path) || 'prestate',
      caseId: '',
      fork: request.fork,
      kind: 'prestate',
    },
    source: raw,
    request,
    expected: emptyExpected(),
  };
}

function adaptHex(raw: string, options: LoadFixtureOptions): LoadedFixture {
  const code = asHexBytes(raw.trim());
  const to = '0x00000000000000000000000000000000000000aa';
  const request: TraceJournalRequest = {
    fork: options.preferredFork ?? 'Osaka',
    transaction: {
      from: '0x0000000000000000000000000000000000000001',
      to,
      gasLimit: '0x989680',
      value: '0x0',
      data: '0x',
    },
    preState: [],
  };
  return {
    identity: {
      path: options.path ?? '',
      name: fileName(options.path) || 'bytecode',
      caseId: '',
      fork: request.fork,
      kind: 'hex',
    },
    source: raw,
    request: { ...request, code },
    expected: emptyExpected(),
  };
}

function pickCase(root: Record<string, unknown>, caseId?: string): { name: string; node: Record<string, unknown> } {
  if (isCaseNode(root))
    return { name: 'fixture', node: root };

  let fallback: { name: string; node: Record<string, unknown> } | null = null;
  for (const [name, value] of Object.entries(root)) {
    if (!isRecord(value) || !isCaseNode(value)) continue;
    if (caseId && (name === caseId || name.includes(caseId)))
      return { name, node: value };
    fallback ??= { name, node: value };
  }
  if (fallback) return fallback;
  throw new FixtureAdapterError('No state_test case found (need env + pre + transaction + post).');
}

function isCaseNode(value: Record<string, unknown>): boolean {
  return 'pre' in value && 'transaction' in value && ('post' in value || 'expect' in value);
}

function pickPostVariant(
  post: Record<string, unknown> | null,
  preferredFork?: string,
): { fork: string; variant: Record<string, unknown> | null } {
  if (!post) return { fork: preferredFork ?? 'Osaka', variant: null };

  const tryFork = (name: string) => {
    const arr = post[name];
    if (!Array.isArray(arr) || arr.length === 0 || !isRecord(arr[0])) return null;
    return { fork: name, variant: arr[0] };
  };

  if (preferredFork) {
    const hit = tryFork(preferredFork);
    if (hit) return hit;
  }
  for (const [name, value] of Object.entries(post)) {
    if (Array.isArray(value) && value.length > 0 && isRecord(value[0]))
      return { fork: name, variant: value[0] };
  }
  throw new FixtureAdapterError('No post[] variant for any known fork.');
}

function readPreAccounts(pre: Record<string, unknown>): TracePreStateAccount[] {
  return Object.entries(pre).map(([address, value]) => {
    if (!isRecord(value))
      throw new FixtureAdapterError(`pre[${address}] is not an object.`);
    return {
      address,
      nonce: optionalQty(value.nonce) ?? '0x0',
      balance: optionalQty(value.balance) ?? '0x0',
      code: asHexBytes(str(value.code) ?? '0x'),
      storage: asStorage(value.storage),
    };
  });
}

function readExpected(variant: Record<string, unknown> | null): FixtureExpected {
  if (!variant) return emptyExpected();
  const exception = str(variant.expectException) ?? null;
  const postState: ExpectedAccount[] = [];
  if (isRecord(variant.state)) {
    for (const [address, value] of Object.entries(variant.state)) {
      if (!isRecord(value)) continue;
      postState.push({
        address,
        nonce: optionalQty(value.nonce) ?? '0x0',
        balance: optionalQty(value.balance) ?? '0x0',
        code: asHexBytes(str(value.code) ?? '0x'),
        storage: asStorage(value.storage),
      });
    }
  }
  return {
    success: !exception,
    exception,
    postHash: str(variant.hash) ?? null,
    postState,
  };
}

function emptyExpected(): FixtureExpected {
  return { success: true, exception: null, postHash: null, postState: [] };
}

function adaptAuthorizations(raw: unknown): TraceAuthorization[] | undefined {
  if (!Array.isArray(raw) || raw.length === 0) return undefined;
  return raw.map((entry, i) => {
    if (!isRecord(entry))
      throw new FixtureAdapterError(`authorizationList[${i}] is not an object.`);
    const signer = str(entry.signer);
    const address = str(entry.address) ?? str(entry.delegate);
    if (!address)
      throw new FixtureAdapterError(`authorizationList[${i}] missing delegate address.`);
    const auth: TraceAuthorization = {
      chainId: optionalQty(entry.chainId) ?? '0x0',
      address,
      nonce: optionalQty(entry.nonce) ?? '0x0',
      valid: Boolean(signer),
    };
    if (signer) auth.signer = signer;
    return auth;
  });
}

function variantAccessList(tx: Record<string, unknown>, dataIndex: number) {
  const lists = tx.accessLists ?? tx.accessList;
  if (!Array.isArray(lists) || lists.length === 0) return undefined;
  const first = lists[0];
  const list = Array.isArray(first) ? lists[Math.min(dataIndex, lists.length - 1)] : lists;
  if (!Array.isArray(list) || list.length === 0) return undefined;
  return list.flatMap((entry) => {
    if (!isRecord(entry) || typeof entry.address !== 'string') return [];
    const keys = Array.isArray(entry.storageKeys)
      ? entry.storageKeys.map((key) => qty(key) ?? '0x0')
      : [];
    return [{ address: entry.address, storageKeys: keys }];
  });
}

function variantBytes(value: unknown, index: number): string {
  const picked = pickVariant(value, index);
  return asHexBytes(typeof picked === 'string' ? picked : '0x');
}

function variantQty(value: unknown, index: number): string | undefined {
  const picked = pickVariant(value, index);
  return optionalQty(picked);
}

function pickVariant(value: unknown, index: number): unknown {
  if (!Array.isArray(value)) return value;
  if (value.length === 0) return undefined;
  return value[Math.min(Math.max(index, 0), value.length - 1)];
}

function asIndex(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}

function optionalQty(value: unknown): string | undefined {
  if (value === undefined || value === null || value === '') return undefined;
  return qty(value);
}

function qty(value: unknown): string | undefined {
  if (typeof value === 'number') {
    if (!Number.isInteger(value) || value < 0)
      throw new FixtureAdapterError('Quantity must be an unsigned integer.');
    return '0x' + value.toString(16);
  }
  if (typeof value !== 'string' || value.trim() === '') return undefined;
  const text = value.trim();
  if (text.startsWith('-'))
    throw new FixtureAdapterError('Quantity must be unsigned.');
  if (text.startsWith('0x') || text.startsWith('0X')) {
    const hex = text.slice(2).replace(/^0+/, '') || '0';
    if (!/^[0-9a-f]*$/i.test(hex))
      throw new FixtureAdapterError(`Invalid quantity '${value}'.`);
    return '0x' + hex.toLowerCase();
  }
  if (!/^\d+$/.test(text))
    throw new FixtureAdapterError(`Invalid quantity '${value}'.`);
  return '0x' + BigInt(text).toString(16);
}

function asHexBytes(value: string): string {
  const text = value.trim();
  const hex = text.startsWith('0x') || text.startsWith('0X') ? text.slice(2) : text;
  if (hex.length % 2 !== 0)
    throw new FixtureAdapterError('Hex bytecode has an odd length.');
  if (hex && !/^[0-9a-f]*$/i.test(hex))
    throw new FixtureAdapterError('Invalid hex bytecode.');
  return '0x' + hex.toLowerCase();
}

function asStorage(value: unknown): Record<string, string> {
  if (!isRecord(value)) return {};
  const storage: Record<string, string> = {};
  for (const [key, slot] of Object.entries(value))
    storage[qty(key) ?? key] = optionalQty(slot) ?? '0x0';
  return storage;
}

function asStringArray(value: unknown): string[] | undefined {
  if (!Array.isArray(value) || value.length === 0) return undefined;
  return value.map((item) => {
    if (typeof item !== 'string')
      throw new FixtureAdapterError('blobVersionedHashes entries must be hex strings.');
    return asHexBytes(item);
  });
}

function assertNoForbiddenKeys(request: TraceJournalRequest) {
  const json = JSON.stringify(request);
  for (const key of ['secretKey', 'expectException', '"post"', '"r":', '"s":', 'yParity', 'txbytes']) {
    if (json.includes(key))
      throw new FixtureAdapterError(`Normalized request leaked fixture field ${key}.`);
  }
}

function firstPreAddress(pre: Record<string, unknown>): string | undefined {
  return Object.keys(pre)[0];
}

function str(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function fileName(path?: string): string {
  if (!path) return '';
  const parts = path.replace(/\\/g, '/').split('/');
  return parts[parts.length - 1] ?? path;
}
