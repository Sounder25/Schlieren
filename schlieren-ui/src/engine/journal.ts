import type { JournalTraceResponse } from './store';

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

export function parseJournalTrace(value: unknown): JournalTraceResponse {
  if (!isRecord(value)) throw new Error('Journal trace response is not an object');
  if (!isRecord(value.execution)) throw new Error('Journal trace response is missing execution');
  if (!Array.isArray(value.events) || !Array.isArray(value.frames) || !Array.isArray(value.steps)) {
    throw new Error('Journal trace response is missing event, frame, or step arrays');
  }
  if (!isRecord(value.gasTree) || !isRecord(value.conservation)) {
    throw new Error('Journal trace response is missing gas accounting');
  }
  return value as unknown as JournalTraceResponse;
}
