/**
 * Minimal Muscle smoke: chainId, funded account, deploy Counter, read/write.
 * Requires Schlieren listening with Anvil test mnemonic (see hardhat.config.js).
 */
const hre = require("hardhat");

async function main() {
  const net = await hre.ethers.provider.getNetwork();
  console.log("chainId:", net.chainId.toString());
  if (net.chainId !== 31337n) {
    throw new Error(`expected chainId 31337, got ${net.chainId}`);
  }

  const [signer] = await hre.ethers.getSigners();
  const bal = await hre.ethers.provider.getBalance(signer.address);
  console.log("signer:", signer.address, "balance wei:", bal.toString());
  if (bal === 0n) {
    throw new Error(
      "signer balance is 0 — restart Schlieren with the Anvil test mnemonic (see muscle/README.md)"
    );
  }

  const Counter = await hre.ethers.getContractFactory("Counter");
  const counter = await Counter.deploy();
  await counter.waitForDeployment();
  const addr = await counter.getAddress();
  console.log("deployed Counter at", addr);

  const code = await hre.ethers.provider.getCode(addr);
  if (!code || code === "0x") {
    throw new Error("no code at deployed address");
  }

  await (await counter.increment()).wait();
  const n = await counter.number();
  if (n !== 1n) throw new Error(`expected 1, got ${n}`);

  console.log("MUSCLE SMOKE PASSED");
}

main().catch((e) => {
  console.error(e);
  process.exitCode = 1;
});
