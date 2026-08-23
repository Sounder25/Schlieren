import type { TraceStep } from './store';

interface EelsStep { pc: number; op: string; gas: number; gasCost: number; depth: number; }
export interface EelsDivergence { index: number; field: string; expected: string; actual: string; frameId: number | null; pc: number; op: string; }
export interface EelsAlignment { isAligned: boolean; comparedSteps: number; firstDivergence: EelsDivergence | null; }

function referenceSteps(json: string): EelsStep[] {
  const parsed: unknown = JSON.parse(json);
  const candidate = Array.isArray(parsed)
    ? parsed
    : typeof parsed === 'object' && parsed !== null && 'structLogs' in parsed
      ? (parsed as { structLogs: unknown }).structLogs
      : null;
  if (!Array.isArray(candidate)) throw new Error('Expected an EIP-3155 array or an object with structLogs');
  return candidate as EelsStep[];
}

export function alignEelsTrace(actual: TraceStep[], json: string): EelsAlignment {
  const expected = referenceSteps(json);
  const shared = Math.min(actual.length, expected.length);
  for (let index = 0; index < shared; index++) {
    const a = actual[index];
    const e = expected[index];
    const fields: Array<[string, unknown, unknown]> = [
      ['pc', e.pc, a.pc], ['op', e.op?.toUpperCase(), a.op.toUpperCase()],
      ['gas', e.gas, a.gasBefore], ['gasCost', e.gasCost, a.gasCost], ['depth', e.depth, a.depth],
    ];
    const mismatch = fields.find(([, left, right]) => left !== right);
    if (mismatch) return { isAligned: false, comparedSteps: index, firstDivergence: { index, field: mismatch[0], expected: String(mismatch[1]), actual: String(mismatch[2]), frameId: a.frameId, pc: a.pc, op: a.op } };
  }
  if (actual.length !== expected.length) {
    const context = actual[shared] ?? actual.at(-1);
    return { isAligned: false, comparedSteps: shared, firstDivergence: { index: shared, field: 'length', expected: String(expected.length), actual: String(actual.length), frameId: context?.frameId ?? null, pc: context?.pc ?? -1, op: context?.op ?? 'END' } };
  }
  return { isAligned: true, comparedSteps: shared, firstDivergence: null };
}
