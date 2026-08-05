---
name: eels-log-auditor
description: >
  Audits event log emissions across EELS fixtures, checking log topic
  well-formedness (32-byte 0x-prefixed hex), data payload encoding, contract
  addresses, and comparing emitted logs against expected fixture log topics/data.
---

# Skill: eels-log-auditor

## Purpose
Audit event log emissions produced during EVM execution to verify event topic
hashes, unindexed data encoding, and log expectation matching.

## Usage

```powershell
$env:EELS_FIXTURES_ROOT  = "C:/projects/Scrutor/fixtures/state_tests/cancun"
$env:EELS_INCLUDE_SUBDIRS = "1"
$env:EELS_MAX_CASES      = "9999"
dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj --filter "EelsLogAudit"
```
