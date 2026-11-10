# Denies shell calls that go wrong on this machine: perl anywhere, python in Bash, and native text
# tools run from PowerShell with a double quote, which Windows PowerShell 5.1 strips from arguments.
# Registered on PreToolUse with matcher Bash|PowerShell.

$ErrorActionPreference = 'Stop'

$raw = [Console]::In.ReadToEnd()
if (-not $raw) { exit 0 }

try { $call = $raw | ConvertFrom-Json } catch { exit 0 }
$tool = $call.tool_name
$command = [string]$call.tool_input.command
if (-not $command) { exit 0 }

# A program in command position: line start or after | ; & ( { , optionally `& 'path\`.
function Get-Calls([string]$names) {
    $pattern = '(?m)(^|[|;&({])\s*(&\s*)?[''"]?([^\s''"|;&]*[\\/])?(' + $names + ')(\.exe)?[''"]?(?=\s|$|[|;)])'
    [regex]::Matches($command, $pattern)
}

$reason = $null

if (Get-Calls 'perl[0-9.]*') {
    $reason = 'Perl is not used in this repo. Use Edit/Write, PowerShell, Python, or sed in Bash.'
}
elseif ($tool -eq 'Bash' -and (Get-Calls 'python[0-9.]*|py|pip[0-9.]*')) {
    $reason = 'Python is blocked in Bash. Use the PowerShell tool instead.'
}
elseif ($tool -eq 'PowerShell') {
    foreach ($m in Get-Calls 'sed|awk|gawk|grep|egrep|fgrep|cut|tr|xargs|jq') {
        # The arguments of this call: up to the next pipe, semicolon or line end outside quotes.
        $rest = [regex]::Match($command.Substring($m.Index + $m.Length), '^(''[^'']*''|"[^"]*"?|[^''"|;\r\n])*').Value
        if ($rest -match '"') {
            $reason = "Windows PowerShell 5.1 strips double quotes from native program arguments, so this $($m.Groups[4].Value) call would get a different script. Run it in the Bash tool, or use Edit."
            break
        }
    }
}

if (-not $reason) { exit 0 }

@{
    hookSpecificOutput = @{
        hookEventName            = 'PreToolUse'
        permissionDecision       = 'deny'
        permissionDecisionReason = $reason
    }
} | ConvertTo-Json -Depth 5 -Compress
exit 0
