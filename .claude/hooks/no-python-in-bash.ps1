# Denies Bash calls that invoke python/pip so scripting stays on PowerShell.
# Registered on PreToolUse with matcher Bash.

$ErrorActionPreference = 'Stop'

$raw = [Console]::In.ReadToEnd()
if (-not $raw) { exit 0 }

try { $command = ($raw | ConvertFrom-Json).tool_input.command } catch { exit 0 }

# Only in command position (start, or after | ; & ( ) - otherwise `grep python x` trips it.
if ($command -notmatch '(^|[|;&(])\s*(python[0-9.]*|py|pip[0-9.]*)(\.exe)?\b') { exit 0 }

@{
    hookSpecificOutput = @{
        hookEventName            = 'PreToolUse'
        permissionDecision       = 'deny'
        permissionDecisionReason = 'Python is blocked in Bash. Use the PowerShell tool instead.'
    }
} | ConvertTo-Json -Depth 5 -Compress
exit 0
