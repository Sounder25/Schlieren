# EELS Semantic Provenance — Design Amendment

**Date:** 2026-08-28
**Supersedes:** Console-launcher SHA-256 as primary oracle identity
**Reason:** The pip-generated console_scripts launcher is a ~40KB PE wrapper whose hash changes on venv recreation, pip version change, or editable reinstall. It does not identify the oracle's execution behavior. The previous identity model pinned this hash, creating an unverifiable dependency.

## Problem Statement

The frozen v1 campaign manifests pin `executableSha256: c2a25c7f...`. The only available EELS 2.19.0 installation produces launcher hash `ee46923d...`. No binary matching the v1 hash exists on this machine. The original venv (`C:/projects/eels-venv/`) was deleted.

Additionally, `ethereum-spec-evm.exe --version` reports the git commit of the *working directory*, not the EELS source tree — proving it inherits ambient state rather than reporting its own provenance.

## Corrected Identity Model

Oracle identity is semantic, not launcher-binary:

```csharp
public sealed record EelsSemanticIdentity(
    string PackageName,           // "ethereum-execution"
    string PackageVersion,        // "2.19.0"
    string SourceTreeSha256,      // hash of all .py files in src/ethereum/
    string EvmToolsSha256,        // hash of all .py files in src/ethereum_spec_tools/evm_tools/
    string SourceCommit,          // git rev-parse HEAD of the execution-specs repo
    string PythonVersion,         // "3.13.11"
    string RuntimePlatform,       // "win32"
    IReadOnlyDictionary<string, string> DependencyVersions);
```

The launcher hash is retained as metadata but is no longer a certification gate.

## Current Installation Facts

| Field | Value |
|---|---|
| Package | ethereum-execution |
| Version | 2.19.0 |
| Install type | editable (`file:///C:/projects/execution-specs`) |
| Source commit | `85aa48c742c38a2d5a876f84ebf8082a50273064` |
| Source tree SHA-256 (src/ethereum/**/*.py) | `793296a2492e4c6f4d70679f9a73aa2d03ef19f68058465492555a37b9912c49` |
| EVM tools SHA-256 (src/ethereum_spec_tools/evm_tools/**/*.py) | `9e7ec26512f4feb9f30b76488e99ab5a3f9340b5f377249164ec1f53dc69c711` |
| Python | 3.13.11 (MSC v.1944 64-bit) |
| Launcher SHA-256 | `ee46923d2cfd47457f6324aeb5c21a5d42e363de2ed8597bbbcb69abcc56ee0f` |
| RECORD semantic hash | `b0605fa5990434452791ec5fd648ca9aa7f6350fd0dc86f32cad83d755cb8996` |

### Runtime Dependencies

| Package | Version |
|---|---|
| cryptography | 45.0.7 |
| ethereum-rlp | 0.1.7 |
| ethereum-types | 0.4.1 |
| platformdirs | 4.11.0 |
| py-ecc | 8.0.0 |
| pycryptodome | 3.23.0 |
| spec256k1 | 0.2.3 |
| typing_extensions | 4.15.0 |

## Manifest Versioning

- v1 manifests remain frozen and unchanged. Their `executableSha256` field is documented as "unverifiable — pinned launcher unavailable."
- v2 manifests use `EelsSemanticIdentity` as the oracle identity.
- v2 manifests carry the same 50 frozen cases. Only the identity model changes.
- A v2 manifest is created by copying the v1 case list and attaching the current semantic identity.
- Equivalence between v1 and v2 is established by running deterministic probe fixtures and comparing outputs, not by matching launcher hashes.

## Equivalence Verification

Before accepting v2 as equivalent to v1 for certification purposes:

1. Select 5 deterministic probe cases from the existing Return Data manifest (cases with simple, verifiable post-states).
2. Run each through the current EELS installation.
3. Compare EELS stdout JSON (pass/stateRoot) against the historical v1 run records.
4. If all probes produce identical oracle outputs, the installation is semantically equivalent.
5. If any probe diverges, stop — the installation is not equivalent.

## Implementation Sequence

1. Add `EelsSemanticIdentity` to `Schlieren.Harvest/Domain/HarvestTypes.cs`.
2. Add `EelsProvenanceProbe` utility to compute semantic identity from a configured EELS installation.
3. Add v2 manifest schema that carries semantic identity alongside the frozen case list.
4. Create the v2 Return Data manifest.
5. Run equivalence probes.
6. Resume Task 2 against the v2 manifest.
