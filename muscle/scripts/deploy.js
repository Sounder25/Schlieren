const hre = require("hardhat");

async function main() {
  const [deployer] = await hre.ethers.getSigners();
  const balance = await hre.ethers.provider.getBalance(deployer.address);
  console.log("network:", hre.network.name);
  console.log("deployer:", deployer.address);
  console.log("balance:", hre.ethers.formatEther(balance), "ETH");

  const Counter = await hre.ethers.getContractFactory("Counter");
  const counter = await Counter.deploy();
  await counter.waitForDeployment();
  const address = await counter.getAddress();
  console.log("Counter deployed:", address);

  await (await counter.setNumber(7n)).wait();
  await (await counter.increment()).wait();
  const n = await counter.number();
  console.log("number after set(7)+increment:", n.toString());
  if (n !== 8n) {
    throw new Error(`expected number=8, got ${n}`);
  }
  console.log("deploy smoke OK");
}

main().catch((err) => {
  console.error(err);
  process.exitCode = 1;
});
