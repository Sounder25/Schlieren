import os
import subprocess

out = subprocess.run(["dotnet", "test", r"c:\projects\Scrutor\Scrutor.EELS.Tests\Scrutor.EELS.Tests.csproj", "--filter", "FullyQualifiedName~Harness_ExecutesPublishedCases_AndProducesDeterministicReport", "-l", "console;verbosity=normal"], capture_output=True, text=True)

with open("test_failures.log", "w") as f:
    f.write(out.stdout)
    f.write("\n")
    f.write(out.stderr)
