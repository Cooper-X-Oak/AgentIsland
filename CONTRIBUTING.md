# Contributing to AgentIsland

Contributions are very welcome — especially **new AI coding terminal adapters** and **posture / break micro-actions**!

## 1. Add New Posture / Break Actions (No Coding Needed)

Edit [`config/actions.json`](config/actions.json) and add a new entry:

```json
{
  "id": "neck-stretch",
  "category": "neck",
  "title": "Side Neck Stretch",
  "seconds": 30,
  "tip": "Tilt your ear gently toward your shoulder, keep shoulders relaxed."
}
```

Right-click the system tray icon → **Reload UI & Actions** to test immediately without recompiling.

## 2. Add a New AI Terminal Adapter (Cursor, Windsurf, Cline, OpenCode, etc.)

AgentIsland listens to atomic JSON event files dropped into `%LOCALAPPDATA%\AgentIsland\inbox\`.
You don't need any network port or daemon — just invoke `bin\island-push.exe`:

```powershell
.\bin\island-push.exe --source "Cursor" --project "my-app" --title "Needs approval" --body "Confirm shell command" --kind decision --id "cursor-1"
```

Supported `--kind` values:
* `working` — Blue breathing status pill
* `decision` / `approval` — Amber expanded alert card
* `info` — Blue expanded reply/done card
* `error` — Red expanded error card
* `clear` — Remove active status by `--id`
* `health` — Trigger posture break card

Create a new folder under `adapters/<tool-name>/` with `install.ps1`, `uninstall.ps1`, and a short `README.md`.

## 3. Code Changes & Mandatory Verification

Before opening a PR that modifies `src/` or `ui/`:

```powershell
.\verify.ps1 -Rebuild
```

Ensure all **22/22 automated checks** pass (DirectComposition `hr=0`, real `WindowFromPoint` click-through, `edgeMaxA(T=0,B=0,L=0,R=0)`, zero window jump on morph, zero frontend JS errors) and inspect the generated `verify-sheet.png`.
