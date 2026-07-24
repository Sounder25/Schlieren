#!/usr/bin/env python3
"""
Run basic_tload_after_store test and produce a clean PC-level gas trace.
"""

import subprocess
import sys
import json

# Run the test with tracing enabled
result = subprocess.run([
    "dotnet", "test",
    "Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj",
    "--filter", "FullyQualifiedName~basic_tload_after_store",
    "--logger", "console;verbosity=detailed",
    "--",
    "-v", "detailed"
], capture_output=True, text=True, cwd="/c/projects/Scrutor")

print("=== SCRUTOR TRACE ===")
print(result.stdout)
print(result.stderr, file=sys.stderr)
