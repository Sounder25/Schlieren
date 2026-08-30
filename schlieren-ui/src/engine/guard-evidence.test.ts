import { describe, expect, it } from 'vitest';
import { loadGuardEvidence, looksLikeGuardEvidence } from './guard-evidence';

const bundle = JSON.stringify({
  kind: 'schlieren-guard-evidence',
  workbench: {
    method: 'schlieren_traceJournal',
    causalFrameId: 7,
    headline: 'SELL BLOCKED',
    detail: 'Buy committed and the same-wallet sell reverted.',
    params: [{
      fork: 'Prague',
      transaction: {
        from: '0x67000000000000000000000000000000000000aa',
        to: '0x2200000000000000000000000000000000000002',
        gasLimit: '0x1e8480',
        value: '0x0',
        data: '0x01',
      },
      preState: [{
        address: '0x2200000000000000000000000000000000000002',
        nonce: '0x0',
        balance: '0x1',
        code: '0x00',
        storage: {},
      }],
      blockContext: { number: '0x1' },
    }],
  },
});

describe('loadGuardEvidence', () => {
  it('recognizes guard evidence', () => {
    expect(looksLikeGuardEvidence(bundle)).toBe(true);
    expect(looksLikeGuardEvidence('{"pre":{}}')).toBe(false);
  });

  it('extracts the nested journal replay for the causal sell', () => {
    const loaded = loadGuardEvidence(bundle);
    expect(loaded.replay.causalFrameId).toBe(7);
    expect(loaded.replay.headline).toBe('SELL BLOCKED');
    expect(loaded.config.to).toBe('0x2200000000000000000000000000000000000002');
    expect(loaded.replay.params[0].preState).toEqual(expect.any(Array));
  });
});
