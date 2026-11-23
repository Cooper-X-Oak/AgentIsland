# Codex CLI & Universal Terminal Adapter

Any terminal AI agent (Codex CLI, Aider, OpenCode, Goose, CI scripts, long-running builds) can push live status to **AgentIsland** with zero ports and zero config using `bin\island-push.exe`.

## Quick Examples (PowerShell)

```powershell
# Mark task as running (blue breathing pill)
.\bin\island-push.exe --source "Codex" --project "api-server" --title "Running" --body "Refactoring auth middleware" --kind working --id "codex-1"

# Notify completion (blue alert card)
.\bin\island-push.exe --source "Codex" --project "api-server" --title "Ready for review" --body "All 42 tests passed" --kind info --id "codex-1"

# Notify approval / blocker (amber alert card)
.\bin\island-push.exe --source "Codex" --project "api-server" --title "Needs approval" --body "Waiting for permission to run migration" --kind decision --id "codex-1"

# Notify error (red alert card)
.\bin\island-push.exe --source "CI" --project "api-server" --title "Build failed" --body "TypeScript check exited with code 2" --kind error --id "ci-1"

# Clear status
.\bin\island-push.exe --id "codex-1" --kind clear
```

## PowerShell Wrapper Function

Add this to your `$PROFILE` to automatically notify AgentIsland whenever any long command finishes:

```powershell
function Watch-Island {
    param([scriptblock]$Script, [string]$Project = (Split-Path -Leaf (Get-Location)))
    $push = "<path-to-AgentIsland>\bin\island-push.exe"
    $id = "cmd-$(Get-Random)"
    & $push --source "Terminal" --project $Project --title "Running" --body "$Script" --kind working --id $id | Out-Null
    try {
        & $Script
        & $push --source "Terminal" --project $Project --title "Done" --body "Command completed" --kind info --id $id | Out-Null
    } catch {
        & $push --source "Terminal" --project $Project --title "Failed" --body "$_" --kind error --id $id | Out-Null
        throw
    }
}
```
