import type { GuardReplay, RunConfig } from './store';

export class GuardEvidenceError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'GuardEvidenceError';
  }
}

export interface LoadedGuardEvidence {
  replay: GuardReplay;
  config: Partial<RunConfig>;
}

export function looksLikeGuardEvidence(text: string): boolean {
  const t = text.trimStart();
  if (!t.startsWith('{')) return false;
  try {
    const parsed = JSON.parse(text) as { kind?: unknown };
    return parsed.kind === 'schlieren-guard-evidence';
  } catch {
    return false;
  }
}

export function loadGuardEvidence(raw: string): LoadedGuardEvidence {
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    throw new GuardEvidenceError('Guard evidence is not valid JSON.');
  }
  if (!isRecord(parsed) || parsed.kind !== 'schlieren-guard-evidence')
    throw new GuardEvidenceError('Not a schlieren-guard-evidence bundle.');

  const workbench = parsed.workbench;
  if (!isRecord(workbench))
    throw new GuardEvidenceError('Evidence is missing a Workbench replay payload.');
  if (workbench.method !== 'schlieren_traceJournal')
    throw new GuardEvidenceError('Evidence replay method must be schlieren_traceJournal.');
  if (!Array.isArray(workbench.params) || workbench.params.length === 0 || !isRecord(workbench.params[0]))
    throw new GuardEvidenceError('Evidence replay is missing schlieren_traceJournal params.');

  const request = workbench.params[0];
  const tx = isRecord(request.transaction) ? request.transaction : {};
  const replay: GuardReplay = {
    method: 'schlieren_traceJournal',
    causalFrameId: asNumber(workbench.causalFrameId),
    headline: typeof workbench.headline === 'string' ? workbench.headline : 'GUARD',
    detail: typeof workbench.detail === 'string' ? workbench.detail : '',
    params: [request],
  };

  return {
    replay,
    config: {
      from: asString(tx.from) ?? '',
      to: asString(tx.to) ?? '',
      calldata: asString(tx.data) ?? '0x',
      value: asString(tx.value) ?? '0x0',
      gasLimit: parseGas(tx.gasLimit) ?? 2_000_000,
      fork: asString(request.fork) ?? 'Prague',
      bytecode: '',
    },
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function asString(value: unknown): string | undefined {
  return typeof value === 'string' ? value : undefined;
}

function asNumber(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

function parseGas(value: unknown): number | undefined {
  if (typeof value !== 'string' || value.length === 0) return undefined;
  const n = value.startsWith('0x') ? Number.parseInt(value, 16) : Number.parseInt(value, 10);
  return Number.isFinite(n) ? n : undefined;
}
