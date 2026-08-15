// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

contract Chaos {

    // 1. The Gas Black Hole (OOG loop)
    function infiniteLoop() public {
        while (true) {
        }
    }

    // 2. The Stack Smasher (Stack overflow / underflow)
    function stackSmasher() public pure {
        assembly {
            invalid()
        }
    }

    // 3. The Call-Depth Bomb (Reentrancy / Recursion Limit)
    function recursiveCall() public {
        (bool success, ) = address(this).call(abi.encodeWithSignature("recursiveCall()"));
        require(success, "Recursion failed");
    }

    // 5. The Memory Expansion Attack
    function memoryExpansionAttack() public pure returns (bytes32) {
        bytes32 val;
        assembly {
            val := mload(0xffffffffffffffffffffffffffffffff)
        }
        return val;
    }

    // 6. Precompile abuse
    function precompileAbuse() public {
        // ecrecover (0x1)
        (bool s1, ) = address(1).call("");
        // sha256 (0x2)
        (bool s2, ) = address(2).call(new bytes(1));
        // ripemd160 (0x3)
        (bool s3, ) = address(3).call{gas: 1}("xxx");
        // identity (0x4)
        (bool s4, ) = address(4).call(new bytes(0xfffff)); // huge input but limited gas
    }

    // 8. Dirty calldata test
    function dirtyCalldata(uint256 amount1, uint256 amount2) public pure returns (uint256) {
        require(amount1 < type(uint256).max, "Max hit");
        return amount1 + amount2;
    }

    // 9. Transient Storage Isolation
    // Skipped: Requires explicit Cancun compilation flag
}

contract SelfDestructTarget {
    uint256 public data;
    constructor() {
        data = 42;
    }
    function destroy() public {
        selfdestruct(payable(msg.sender));
    }
}

contract Create2Factory {
    function deploy(bytes32 salt) public returns (address) {
        bytes memory bytecode = type(SelfDestructTarget).creationCode;
        address addr;
        assembly {
            addr := create2(0, add(bytecode, 0x20), mload(bytecode), salt)
        }
        require(addr != address(0), "Deploy failed");
        return addr;
    }
}
