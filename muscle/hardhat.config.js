require("@nomicfoundation/hardhat-toolbox");

/** @type import('hardhat/config').HardhatUserConfig */
module.exports = {
  solidity: "0.8.24",
  defaultNetwork: "schlieren",
  networks: {
    // Schlieren = Anvil-for-Windows. Start node with the same mnemonic so funded keys match.
    //   ..\Schlieren.CLI\bin\Debug\net8.0\Schlieren.CLI.exe `
    //     --host 127.0.0.1 --port 18545 --accounts 3 --balance 10000 `
    //     --mnemonic "test test test test test test test test test test test junk"
    schlieren: {
      url: process.env.SCHLIEREN_RPC || "http://127.0.0.1:18545",
      chainId: 31337,
      accounts: {
        mnemonic:
          process.env.SCHLIEREN_MNEMONIC ||
          "test test test test test test test test test test test junk",
      },
    },
    hardhat: {
      chainId: 31337,
    },
  },
  paths: {
    sources: "./contracts",
    tests: "./test",
    cache: "./cache",
    artifacts: "./artifacts",
  },
  mocha: {
    timeout: 60_000,
  },
};
