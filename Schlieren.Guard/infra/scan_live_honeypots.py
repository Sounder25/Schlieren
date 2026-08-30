import json
import urllib.request

WETH = "0xc02aaa39b223fe8d0a0e5c4f27ead9083c756cc2"
cands = []
for page in range(1, 8):
    url = f"https://api.geckoterminal.com/api/v2/networks/eth/new_pools?page={page}"
    req = urllib.request.Request(url, headers={"User-Agent": "schlieren-guard"})
    data = json.loads(urllib.request.urlopen(req, timeout=20).read())
    for p in data.get("data", []):
        rel = p["relationships"]
        dex = rel["dex"]["data"]["id"]
        quote = rel["quote_token"]["data"]["id"]
        base = rel["base_token"]["data"]["id"].split("_", 1)[1]
        reserve = float(p["attributes"].get("reserve_in_usd") or 0)
        tx = p["attributes"]["transactions"]["h6"]
        if dex == "uniswap_v2" and quote == "eth_" + WETH and reserve >= 400:
            cands.append((base, p["attributes"]["name"], reserve, tx["buys"], tx["sells"]))

print("CANDS", len(cands))
for base, name, reserve, buys, sells in cands:
    hp = json.loads(
        urllib.request.urlopen(
            urllib.request.Request(
                "https://api.honeypot.is/v2/IsHoneypot?address=" + base,
                headers={"User-Agent": "schlieren"},
            ),
            timeout=20,
        ).read()
    )
    hr = hp.get("honeypotResult") or {}
    sim = hp.get("simulationResult") or {}
    pair = hp.get("pair") or {}
    print(
        name,
        base,
        "geckoliq=",
        round(reserve),
        "buys=",
        buys,
        "sells=",
        sells,
        "isHP=",
        hr.get("isHoneypot"),
        "reason=",
        hr.get("honeypotReason"),
        "sellTax=",
        sim.get("sellTax"),
        "buyTax=",
        sim.get("buyTax"),
        "hpliq=",
        pair.get("liquidity"),
        "router=",
        hp.get("router"),
        "simOk=",
        hp.get("simulationSuccess"),
    )
