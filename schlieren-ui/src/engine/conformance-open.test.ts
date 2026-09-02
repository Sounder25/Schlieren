import { describe, expect, it } from 'vitest';
import { loadFixture } from './fixture-adapter';
import { useAppStore } from './store';

const FIXTURE = `{
  "tests/istanbul/eip1344_chainid/test_chainid.py::test_chainid[fork_Berlin-state_test]": {
    "env": { "currentCoinbase": "0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba" },
    "pre": {
      "0xb32fd55d1dcffe091b3b2c07c06ba3308e95064e": { "nonce": "0x00", "balance": "0x01", "code": "0x", "storage": {} },
      "0xabe9bbd35a69090cd5e70acc90e8d161d530a307": { "nonce": "0x01", "balance": "0x00", "code": "0x4660015500", "storage": {} }
    },
    "transaction": {
      "sender": "0xb32fd55d1dcffe091b3b2c07c06ba3308e95064e",
      "to": "0xabe9bbd35a69090cd5e70acc90e8d161d530a307",
      "gasLimit": ["0x0186a0"],
      "value": ["0x00"],
      "data": ["0x"],
      "gasPrice": "0x3b9aca00"
    },
    "post": { "Berlin": [{ "indexes": { "data": 0, "gas": 0, "value": 0 }, "state": {} }] }
  }
}`;

describe('conformance open in workbench', () => {
  it('loads a suite failure through LoadedFixture without a second pipeline', () => {
    const loaded = loadFixture(FIXTURE, {
      path: 'C:/fixtures/test_chainid.json',
      preferredFork: 'Berlin',
      caseId: 'test_chainid',
    });
    useAppStore.getState().applyLoadedFixture(loaded);
    const state = useAppStore.getState();
    expect(state.loadedFixture?.identity.path).toBe('C:/fixtures/test_chainid.json');
    expect(state.loadedFixture?.source).toContain('"pre"');
    expect(state.loadedFixture?.request.preState.length).toBe(2);
    expect(JSON.stringify(state.loadedFixture?.request)).not.toContain('"post"');
    expect(state.config.fork).toBe('Berlin');
    expect(state.config.to).toBe('0xabe9bbd35a69090cd5e70acc90e8d161d530a307');
  });
});
