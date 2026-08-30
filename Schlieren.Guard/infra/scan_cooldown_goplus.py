import json
import time
import urllib.request

tokens = [
    "0xc25e965ce3d58d30130aa8bd5ffb960cb5129773",
    "0x773b401ccb50c68620dcdea065c23d89340fccaf",
    "0x0c862c4aff0c87f3b4e93350ecf2dd6ada0c5400",
    "0x6d1294fcf0124dfa204461dc548bf5d5feb854af",
    "0x2b775897525582d32b7b9c000a8f4b7acba4b1ce",
    "0x20e212a52acb6a921ce59b5a231136f4c69f95de",
    "0x3d74ded1cc99cb1fde9b159c780a23ae8feae13a",
    "0xac10c3cf62a870ab82f82c61f2eaa28d80246bd4",
    "0x2d1643a5d14fb221521767f2405efbbe4d4603fd",
    "0xae1c6e1f3da993c493ad8c3081dc8da8f6ac8160",
    "0x4476d9ef0778c5eac872d275fd905334073ab352",
    "0xbd9cdabc4c5b41276cbd4bdac3f1ef3f084b8389",
    "0x137d9e2c2e60182b229de13c20f76f5386cc7b1b",
    "0x1d8e794199ce47bf6686d1c76c0514f7799f66a1",
    "0x68a528926ba3cfd9e45ab464bb36786f6c81a446",
    "0x4b101a538beb4b5c34f181d2f3412d22918529a2",
    "0xf0f9d895aca5c8678f706fb8216fa22957685a13",
]

for addr in tokens:
    url = "https://api.gopluslabs.io/api/v1/token_security/1?contract_addresses=" + addr
    try:
        data = json.loads(
            urllib.request.urlopen(
                urllib.request.Request(url, headers={"User-Agent": "schlieren"}),
                timeout=20,
            ).read()
        )
    except Exception as e:
        print(addr, "ERR", e)
        time.sleep(0.5)
        continue
    result = (data.get("result") or {}).get(addr.lower()) or {}
    if not result:
        print(addr, "EMPTY")
        time.sleep(0.4)
        continue
    print(
        result.get("token_symbol"),
        addr,
        "cooldown=",
        result.get("trading_cooldown"),
        "honeypot=",
        result.get("is_honeypot"),
        "buy_tax=",
        result.get("buy_tax"),
        "sell_tax=",
        result.get("sell_tax"),
        "liq=",
        (result.get("dex") or [{}])[0].get("liquidity") if result.get("dex") else None,
        "open=",
        result.get("is_open_source"),
    )
    time.sleep(0.4)
