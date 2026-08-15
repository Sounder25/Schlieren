// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

/// @notice Trivial contract used to prove Muscle (Hardhat) can deploy + call against Schlieren.
contract Counter {
    uint256 public number;

    event Incremented(uint256 newNumber);

    function setNumber(uint256 newNumber) external {
        number = newNumber;
    }

    function increment() external {
        unchecked {
            number += 1;
        }
        emit Incremented(number);
    }
}
