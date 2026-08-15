const { expect } = require("chai");
const { ethers } = require("hardhat");

/**
 * Integration tests against the configured network (default: schlieren).
 * Start Schlieren first with the Anvil test mnemonic — see muscle/README.md.
 */
describe("Counter @ Schlieren", function () {
  let counter;
  let deployer;

  before(async function () {
    [deployer] = await ethers.getSigners();
    const bal = await ethers.provider.getBalance(deployer.address);
    if (bal === 0n) {
      this.skip();
    }
  });

  beforeEach(async function () {
    const Counter = await ethers.getContractFactory("Counter");
    counter = await Counter.deploy();
    await counter.waitForDeployment();
  });

  it("starts at zero", async function () {
    expect(await counter.number()).to.equal(0n);
  });

  it("setNumber then increment", async function () {
    await (await counter.setNumber(41n)).wait();
    await (await counter.increment()).wait();
    expect(await counter.number()).to.equal(42n);
  });

  it("emits Incremented", async function () {
    await expect(counter.increment())
      .to.emit(counter, "Incremented")
      .withArgs(1n);
  });

  it("has contract code on chain", async function () {
    const addr = await counter.getAddress();
    const code = await ethers.provider.getCode(addr);
    expect(code.length).to.be.greaterThan(2);
  });
});
