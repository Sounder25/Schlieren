using System.Numerics;
using Schlieren.Core.Execution;

namespace Schlieren.Core.Gas;

public readonly record struct TransactionGasContext(
    bool IsContractCreation,
    ulong CalldataZeroBytes,
    ulong CalldataNonZeroBytes,
    ulong AccessListAddresses,
    ulong AccessListStorageKeys,
    ulong AuthorizationCount,
    byte TransactionType);

public readonly record struct MemoryGasContext(
    BigInteger CurrentSizeBytes,
    BigInteger Offset,
    BigInteger Length);

public readonly record struct ExponentGasContext(BigInteger Exponent);

public readonly record struct AccessGasContext(
    bool IsWarm,
    bool AccountExists,
    bool HasDelegation,
    bool DelegationIsWarm);

public readonly record struct SstoreGasContext(
    BigInteger OriginalValue,
    BigInteger CurrentValue,
    BigInteger NewValue,
    bool IsWarm,
    ulong GasRemaining);

public readonly record struct CallGasContext(
    CallType CallType,
    ulong ParentGasRemaining,
    ulong RequestedGas,
    bool TransfersValue,
    bool TargetIsWarm,
    bool TargetExists,
    bool ParentIsStatic);

public readonly record struct CreateGasContext(
    bool IsCreate2,
    BigInteger InitCodeOffset,
    BigInteger InitCodeLength,
    ulong ParentGasRemaining,
    bool DestinationIsDeployable,
    bool TransfersValue);

public readonly record struct PrecompileGasContext(
    ushort PrecompileId,
    BigInteger InputLength,
    ulong GasAvailable);

public readonly record struct ExceptionalHaltGasContext(
    EvmError Error,
    ulong FrameGasRemaining);

public readonly record struct SettlementGasContext(
    ulong IntrinsicGas,
    ulong ExecutionGas,
    long RefundCounter,
    ulong RefundQuotient,
    ulong CalldataFloor,
    BigInteger EffectiveGasPrice,
    BigInteger BaseFeePerGas,
    BigInteger BlobFee);