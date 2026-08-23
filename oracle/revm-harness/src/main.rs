use anyhow::{Context as AnyhowContext, Result};
use alloy_primitives::{Address, Bytes, U256};
use revm::{
    context::{Context, TxEnv},
    database::CacheDB,
    database_interface::EmptyDB,
    primitives::{hardfork::SpecId, TxKind, KECCAK_EMPTY},
    state::{AccountInfo, Bytecode},
    ExecuteEvm, MainBuilder, MainContext,
};
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::io::{self, Read, Write};
use std::str::FromStr;

/// Stable JSON contract: Execution request from Schlieren
#[derive(Debug, Deserialize)]
struct ExecutionCase {
    fork: String,
    caller: String,
    target: String,
    #[serde(default = "default_calldata")]
    calldata: String,
    #[serde(default = "default_value")]
    value: String,
    #[serde(default = "default_gas_limit")]
    gas_limit: u64,
    #[serde(default = "default_block_number")]
    block_number: u64,
    #[serde(default = "default_block_timestamp")]
    block_timestamp: u64,
    #[serde(default = "default_block_coinbase")]
    block_coinbase: String,
    #[serde(default = "default_block_difficulty")]
    block_difficulty: String,
    #[serde(default = "default_block_gas_limit")]
    block_gas_limit: u64,
    #[serde(default = "default_block_base_fee")]
    block_base_fee: String,
    prestate: Vec<AccountState>,
}

#[derive(Debug, Deserialize)]
struct AccountState {
    address: String,
    #[serde(default = "default_code")]
    code: String,
    #[serde(default = "default_balance")]
    balance: String,
    #[serde(default)]
    nonce: u64,
    #[serde(default)]
    storage: HashMap<String, String>,
}

/// Stable JSON contract: Execution result returned to Schlieren
#[derive(Debug, Serialize)]
struct ExecutionResponse {
    success: bool,
    gas_used: u64,
    refund: u64,
    return_data: String,
    frames: Vec<ExecutionFrame>,
    logs: Vec<ExecutionLog>,
    state_diff: HashMap<String, AccountStateDiff>,
    #[serde(default)]
    cold_accounts: Vec<String>,
    #[serde(default)]
    warm_accounts: Vec<String>,
    #[serde(default)]
    cold_slots: Vec<String>,
    #[serde(default)]
    warm_slots: Vec<String>,
}

#[derive(Debug, Serialize)]
struct ExecutionFrame {
    depth: u32,
    call_type: String,
    code_address: String,
    context_address: String,
    caller: String,
    value: String,
    gas_provided: u64,
    gas_consumed: u64,
    success: bool,
    return_data: String,
}

#[derive(Debug, Serialize)]
struct ExecutionLog {
    address: String,
    topics: Vec<String>,
    data: String,
}

#[derive(Debug, Serialize)]
struct AccountStateDiff {
    address: String,
    code: String,
    balance: String,
    nonce: u64,
    storage: HashMap<String, String>,
}

// Defaults
fn default_calldata() -> String {
    "0x".to_string()
}
fn default_value() -> String {
    "0x0".to_string()
}
fn default_gas_limit() -> u64 {
    10_000_000
}
fn default_block_number() -> u64 {
    1
}
fn default_block_timestamp() -> u64 {
    1000
}
fn default_block_coinbase() -> String {
    "0x0000000000000000000000000000000000000000".to_string()
}
fn default_block_difficulty() -> String {
    "0x0".to_string()
}
fn default_block_gas_limit() -> u64 {
    30_000_000
}
fn default_block_base_fee() -> String {
    "0xa".to_string()
}
fn default_code() -> String {
    "0x".to_string()
}
fn default_balance() -> String {
    "0x0".to_string()
}

fn main() -> Result<()> {
    // Read JSON from stdin
    let mut input = String::new();
    io::stdin()
        .read_to_string(&mut input)
        .context("Failed to read stdin")?;

    let case: ExecutionCase =
        serde_json::from_str(&input).context("Failed to parse ExecutionCase JSON")?;

    // Execute via revm
    let result = execute_case(case)?;

    // Write result to stdout
    let output =
        serde_json::to_string(&result).context("Failed to serialize ExecutionResponse")?;
    io::stdout()
        .write_all(output.as_bytes())
        .context("Failed to write stdout")?;

    Ok(())
}

fn execute_case(case: ExecutionCase) -> Result<ExecutionResponse> {
    // 1. Create in-memory database
    let mut db = CacheDB::<EmptyDB>::default();

    // 2. Load prestate
    for account in &case.prestate {
        let addr = parse_address(&account.address)?;
        let code = parse_bytes(&account.code)?;
        let balance = parse_u256(&account.balance)?;

        let mut info = AccountInfo {
            balance,
            nonce: account.nonce,
            code_hash: KECCAK_EMPTY,
            code: if code.is_empty() {
                None
            } else {
                Some(Bytecode::new_raw(code))
            },
            account_id: Default::default(), // Let DB assign account ID
        };

        db.insert_account_info(addr, info);

        // Load storage
        for (slot_str, value_str) in &account.storage {
            let slot = parse_u256(slot_str)?;
            let value = parse_u256(value_str)?;
            db.insert_account_storage(addr, slot, value)
                .context("Failed to insert storage")?;
        }
    }

    // 3. Create context with mainnet defaults
    let mut ctx = Context::mainnet().with_db(db);

    // 4. Configure fork (spec) — MUST use set_spec_and_mainnet_gas_params to rebuild gas table
    let spec = parse_fork(&case.fork)?;
    ctx.cfg.set_spec_and_mainnet_gas_params(spec);

    // 5. Configure block environment
    ctx.block.number = U256::from(case.block_number);
    ctx.block.timestamp = U256::from(case.block_timestamp);
    ctx.block.beneficiary = parse_address(&case.block_coinbase)?; // coinbase -> beneficiary in revm 42
    ctx.block.difficulty = parse_u256(&case.block_difficulty)?;
    ctx.block.gas_limit = case.block_gas_limit; // Now u64 directly
    ctx.block.basefee = parse_u256(&case.block_base_fee)?
        .try_into()
        .context("Base fee too large for u64")?; // Now u64 instead of U256

    // 6. Build EVM
    let mut evm = ctx.build_mainnet();

    // 7. Build and execute transaction
    let base_fee_u128 = parse_u256(&case.block_base_fee)?
        .try_into()
        .context("Base fee too large for u128")?;
    
    let tx = TxEnv::builder()
        .caller(parse_address(&case.caller)?)
        .kind(TxKind::Call(parse_address(&case.target)?))
        .data(parse_bytes(&case.calldata)?)
        .value(parse_u256(&case.value)?)
        .gas_limit(case.gas_limit)
        .gas_price(base_fee_u128) // gas_price is u128
        .nonce(0)
        .build()
        .context("Failed to build transaction")?;

    let exec_result = evm.transact(tx).context("Execution failed")?;

    // 8. Extract execution details
    let success = exec_result.result.is_success();
    let gas_used = exec_result.result.tx_gas_used(); // After refund (receipt value)
    let result_gas = exec_result.result.gas();
    let refund = result_gas.inner_refunded(); // Actual refund REVM computed
    let total_gas_spent = result_gas.total_gas_spent(); // Before refund
    // Diagnostic: print to stderr so it doesn't corrupt JSON stdout
    eprintln!("DIAG: total_gas_spent={}, refund={}, tx_gas_used={}, floor_gas={}", 
              total_gas_spent, refund, gas_used, result_gas.floor_gas());
    let return_data = match exec_result.result.output() {
        Some(bytes) => format!("0x{}", hex::encode(bytes)),
        None => "0x".to_string(),
    };

    // 9. Extract logs
    let logs = exec_result
        .result
        .logs()
        .iter()
        .map(|log| ExecutionLog {
            address: format!("0x{:x}", log.address),
            topics: log
                .topics()
                .iter()
                .map(|t| format!("0x{:x}", t))
                .collect(),
            data: format!("0x{}", hex::encode(log.data.data.as_ref())),
        })
        .collect();

    // 10. Build state diff — include storage changes per account
    let mut state_diff = HashMap::new();
    for (addr, account) in &exec_result.state {
        // Skip accounts that weren't touched (status == Default)
        // revm marks touched accounts; we include all to be safe, 
        // but skip the zero address (coinbase with no balance)
        
        let mut storage_map = HashMap::new();
        for (slot, slot_value) in &account.storage {
            // Only include slots that were actually changed
            if slot_value.is_changed() {
                storage_map.insert(
                    format!("0x{:x}", slot),
                    format!("0x{:x}", slot_value.present_value()),
                );
            }
        }

        state_diff.insert(
            format!("0x{:x}", addr),
            AccountStateDiff {
                address: format!("0x{:x}", addr),
                code: "0x".to_string(),
                balance: format!("0x{:x}", account.info.balance),
                nonce: account.info.nonce,
                storage: storage_map,
            },
        );
    }

    // 11. Build result (frame tree TBD — requires inspector)
    Ok(ExecutionResponse {
        success,
        gas_used,
        refund,
        return_data,
        frames: vec![], // TODO: extract from inspector
        logs,
        state_diff,
        cold_accounts: vec![],
        warm_accounts: vec![],
        cold_slots: vec![],
        warm_slots: vec![],
    })
}

fn parse_address(s: &str) -> Result<Address> {
    Address::from_str(s.trim_start_matches("0x"))
        .with_context(|| format!("Invalid address: {}", s))
}

fn parse_bytes(s: &str) -> Result<Bytes> {
    let hex = s.trim_start_matches("0x");
    if hex.is_empty() {
        return Ok(Bytes::new());
    }
    let bytes = hex::decode(hex).with_context(|| format!("Invalid hex: {}", s))?;
    Ok(Bytes::from(bytes))
}

fn parse_u256(s: &str) -> Result<U256> {
    let hex = s.trim_start_matches("0x");
    if hex.is_empty() || hex == "0" {
        return Ok(U256::ZERO);
    }
    U256::from_str_radix(hex, 16).with_context(|| format!("Invalid U256: {}", s))
}

fn parse_fork(fork: &str) -> Result<SpecId> {
    match fork.to_uppercase().as_str() {
        "CANCUN"                          => Ok(SpecId::CANCUN),
        "PRAGUE"                          => Ok(SpecId::PRAGUE),
        "SHANGHAI"                        => Ok(SpecId::SHANGHAI),
        "PARIS" | "MERGE"                 => Ok(SpecId::MERGE),
        "LONDON"                          => Ok(SpecId::LONDON),
        "BERLIN"                          => Ok(SpecId::BERLIN),
        "ISTANBUL"                        => Ok(SpecId::ISTANBUL),
        "PETERSBURG" | "CONSTANTINOPLE"   => Ok(SpecId::PETERSBURG),
        "BYZANTIUM"                       => Ok(SpecId::BYZANTIUM),
        "SPURIOUS_DRAGON" | "SPURIOUSDRAGON" | "EIP158" => Ok(SpecId::SPURIOUS_DRAGON),
        "TANGERINE" | "TANGERINEWHISTLE" | "EIP150"     => Ok(SpecId::TANGERINE),
        "HOMESTEAD"                       => Ok(SpecId::HOMESTEAD),
        "FRONTIER"                        => Ok(SpecId::FRONTIER),
        _ => anyhow::bail!("Unknown fork: {}", fork),
    }
}
