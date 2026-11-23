# Codex CLI & Universal Terminal Adapter

## Option 1: One-Click Native Hooks (`~/.codex/hooks.json`, Recommended for Codex CLI 0.171+)

Safely appends AgentIsland hooks (`UserPromptSubmit`, `PermissionRequest`, `Stop`, `Interrupt`) into `~/.codex/hooks.json` without touching your existing hooks or `config.toml` `notify` setting:

```powershell
.\adapters\codex-cli\install.ps1
```

Uninstall cleanly:
```powershell
.\adapters\codex-cli\uninstall.ps1
```

## Option 2: Legacy `config.toml` `notify` Hook

If you use an older Codex CLI build without `hooks.json`, add this line at the **top** of `~/.codex/config.toml` (before any `[section]` headers):

```toml
notify = ["<path-to-AgentIsland>\\bin\\island-hook.exe", "--adapter", "codex"]
```

## Option 3: Universal CLI Push (`island-push.exe`) for Any Terminal / Script

```powershell
.\bin\island-push.exe --source "Codex" --project "api-server" --title "Ready for review" --body "All 42 tests passed" --kind info --id "codex-1"
```
