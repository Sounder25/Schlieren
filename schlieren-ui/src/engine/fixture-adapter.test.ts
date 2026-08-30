import { describe, expect, it } from 'vitest';
import { FixtureAdapterError, loadFixture } from './fixture-adapter';

const STATE_TEST = `{
  "tests/istanbul/eip1344_chainid/test_chainid.py::test_chainid[fork_Berlin-state_test]": {
    "env": {
      "currentCoinbase": "0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba",
      "currentGasLimit": "0x07270e00",
      "currentNumber": "0x01",
      "currentTimestamp": "0x03e8",
      "currentDifficulty": "0x020000"
    },
    "pre": {
      "0xb32fd55d1dcffe091b3b2c07c06ba3308e95064e": {
        "nonce": "0x00",
        "balance": "0x3635c9adc5dea00000",
        "code": "0x",
        "storage": {}
      },
      "0xabe9bbd35a69090cd5e70acc90e8d161d530a307": {
        "nonce": "0x01",
        "balance": "0x00",
        "code": "0x4660015500",
        "storage": {}
      }
    },
    "transaction": {
      "nonce": "0x00",
      "gasPrice": "0x3b9aca00",
      "gasLimit": ["0x0186a0"],
      "to": "0xabe9bbd35a69090cd5e70acc90e8d161d530a307",
      "value": ["0x00"],
      "data": ["0x01"],
      "sender": "0xb32fd55d1dcffe091b3b2c07c06ba3308e95064e",
      "secretKey": "0x62b3a3d0a9dd4a0016308930abe9bbd35a69090cd5e70acc90e8d161d530a307"
    },
    "post": {
      "Berlin": [{
        "hash": "0x2d3f83b5cf4b3ef86d7fd204a844d06635a355596db072d6d12775b3b93ea4d4",
        "indexes": { "data": 0, "gas": 0, "value": 0 },
        "state": {
          "0xabe9bbd35a69090cd5e70acc90e8d161d530a307": {
            "nonce": "0x01",
            "balance": "0x00",
            "code": "0x4660015500",
            "storage": { "0x01": "0x01" }
          }
        }
      }]
    },
    "config": { "chainid": "0x01" }
  }
}`;

const AUTH_TEST = `{
  "case": {
    "env": { "currentCoinbase": "0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba" },
    "pre": {
      "0x78d03ebeca16df0be46069103a22faeaf727cb48": {
        "nonce": "0x00", "balance": "0x01", "code": "0x", "storage": {}
      }
    },
    "transaction": {
      "sender": "0x78d03ebeca16df0be46069103a22faeaf727cb48",
      "to": "0x78d03ebeca16df0be46069103a22faeaf727cb48",
      "gasLimit": ["0x01b6bb"],
      "value": ["0x01"],
      "data": ["0x"],
      "maxFeePerGas": "0x07",
      "maxPriorityFeePerGas": "0x00",
      "authorizationList": [{
        "chainId": "0x00",
        "address": "0xfab860e17f926f7cdb3c2cf02d0646e9fefb076b",
        "nonce": "0x01",
        "v": "0x00",
        "r": "0x3361aac6278699c96b2f068db52d9905fda1ae1afe5631e5f6ea054c392f547d",
        "s": "0x3061a175659117fed7b98162dd29d88bb8e2bd99cfb91f1eb58f78077c6eaec3",
        "signer": "0x78d03ebeca16df0be46069103a22faeaf727cb48",
        "yParity": "0x00"
      }],
      "secretKey": "0x655482cfa3d627b75c7d2839fab860e17f926f7cdb3c2cf02d0646e9fefb076b"
    },
    "post": {
      "Prague": [{
        "hash": "0x99caed3e70eec0eddcc3e8b5d7ee4edf6376825126ddf7c1ca6ee199d2ca8169",
        "indexes": { "data": 0, "gas": 0, "value": 0 },
        "state": {}
      }]
    }
  }
}`;

describe('fixture adapter', () => {
  it('flattens state-test arrays into a nested journal request and keeps expected post off the request', () => {
    const loaded = loadFixture(STATE_TEST, { path: 'test_chainid.json', preferredFork: 'Berlin' });
    expect(loaded.identity.kind).toBe('state-test');
    expect(loaded.identity.fork).toBe('Berlin');
    expect(loaded.identity.path).toBe('test_chainid.json');
    expect(loaded.source).toContain('secretKey');
    expect(loaded.request.transaction.gasLimit).toBe('0x186a0');
    expect(loaded.request.transaction.value).toBe('0x0');
    expect(loaded.request.transaction.data).toBe('0x01');
    expect(loaded.request.transaction.from).toBe('0xb32fd55d1dcffe091b3b2c07c06ba3308e95064e');
    expect(loaded.request.preState).toHaveLength(2);
    expect(loaded.request.blockContext?.chainId).toBe('0x1');
    expect(loaded.expected.postState[0]?.storage['0x1']).toBe('0x1');
    expect(loaded.expected.postHash).toMatch(/^0x2d3f/);
    const sent = JSON.stringify(loaded.request);
    expect(sent).not.toContain('secretKey');
    expect(sent).not.toContain('expectException');
    expect(JSON.parse(sent).expected).toBeUndefined();
    expect(JSON.parse(sent).post).toBeUndefined();
  });

  it('strips raw 7702 signatures and keeps recovered signer as normalized authorization', () => {
    const loaded = loadFixture(AUTH_TEST);
    const auth = loaded.request.transaction.authorizationList?.[0];
    expect(loaded.request.transaction.type).toBe('0x4');
    expect(auth?.signer).toBe('0x78d03ebeca16df0be46069103a22faeaf727cb48');
    expect(auth?.valid).toBe(true);
    expect(auth?.address).toBe('0xfab860e17f926f7cdb3c2cf02d0646e9fefb076b');
    const sent = JSON.stringify(loaded.request);
    expect(sent).not.toContain('yParity');
    expect(sent).not.toContain('"r":');
    expect(sent).not.toContain('secretKey');
    expect(loaded.source).toContain('yParity');
  });

  it('loads workbench pre-state without inventing expected post', () => {
    const loaded = loadFixture(JSON.stringify({
      accounts: [{ address: '0x00000000000000000000000000000000000000aa', balance: '100', code: '0x6000' }],
    }));
    expect(loaded.identity.kind).toBe('prestate');
    expect(loaded.request.preState[0]?.balance).toBe('0x64');
    expect(loaded.expected.postState).toEqual([]);
  });

  it('parses hex nonce instead of silently zeroing it', () => {
    const loaded = loadFixture(JSON.stringify({
      accounts: [{
        address: '0x00000000000000000000000000000000000000aa',
        nonce: '0x5',
        balance: '0x01',
        code: '0x00',
      }],
    }));
    expect(loaded.request.preState[0]?.nonce).toBe('0x5');
  });

  it('rejects unknown text', () => {
    expect(() => loadFixture('not a fixture')).toThrow(FixtureAdapterError);
  });
});
