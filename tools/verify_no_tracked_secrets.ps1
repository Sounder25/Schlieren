# verify_no_tracked_secrets.ps1
# Scans git-tracked text files for JWT-shaped strings and known secret assignments.
# Prints file + line with a redacted fingerprint only (never the full secret value).
# Exits 0 if no findings; exits 1 if any finding is detected.
#
# Run from the repository root:
#   powershell -File tools/verify_no_tracked_secrets.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$findings = 0

Write-Host "Running secret scan over git-tracked files..."

# Pattern 1: JWT-shaped strings (eyJ header)
# Uses git grep so regex is handled by git's engine, not PowerShell's parser.
$jwtMatches = & git grep -n -P "eyJ[A-Za-z0-9_\-]{20,}\.[A-Za-z0-9_\-]{20,}\.[A-Za-z0-9_\-]{10,}" 2>$null
foreach ($match in $jwtMatches) {
    # Redact: print only the first 12 chars of the token
    $parts = $match -split ":", 3
    if ($parts.Count -ge 3) {
        $context = $parts[2]
        if ($context -match "eyJ([A-Za-z0-9_\-]+)") {
            $fragment = "eyJ" + $Matches[1].Substring(0, [Math]::Min(9, $Matches[1].Length)) + "...[REDACTED]"
        } else {
            $fragment = "[REDACTED]"
        }
        Write-Host "FINDING: $($parts[0]):$($parts[1])  JWT-shaped credential — $fragment"
        $findings++
    }
}

# Pattern 2: known credential variable names assigned to non-empty string literals
# e.g.  N8nApiKey = "...",  McpToken = "...",  const string N8nApiKey = "..."
$credMatches = & git grep -n -P "(?:N8nApiKey|McpToken|BearerToken|ApiSecret|AuthToken)\s*=\s*""[^""]+" 2>$null
foreach ($match in $credMatches) {
    $parts = $match -split ":", 3
    if ($parts.Count -ge 3) {
        $context = $parts[2].Trim()
        # Extract value after = "
        if ($context -match '=\s*"(.{4})') {
            $fragment = $Matches[1] + "...[REDACTED]"
        } else {
            $fragment = "[REDACTED]"
        }
        Write-Host "FINDING: $($parts[0]):$($parts[1])  Credential assignment — $fragment"
        $findings++
    }
}

# Pattern 3: hard-coded corpus path as a C# const/field string literal
$corpusMatches = & git grep -n -P "CorpusDir\s*=\s*@?" 2>$null
foreach ($match in $corpusMatches) {
    $parts = $match -split ":", 3
    if ($parts.Count -ge 3) {
        $context = $parts[2].Trim()
        Write-Host "FINDING: $($parts[0]):$($parts[1])  Hard-coded corpus path — $context"
        $findings++
    }
}

if ($findings -eq 0) {
    Write-Host "Secret scan: no findings. Exit 0."
    exit 0
} else {
    Write-Host "Secret scan: $findings finding(s). Exit 1."
    exit 1
}
