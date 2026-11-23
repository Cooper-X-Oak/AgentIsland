<div align="center">

# AgentIsland

**A 68 KB Native Windows 11 Dynamic Island for AI Coding Agents & Posture Coach**

[English](README.md) · [简体中文](README.zh-CN.md)

[![Platform](https://img.shields.io/badge/Windows-11%20x64-0078D6?style=flat-square)](https://github.com/Cooper-X-Oak/AgentIsland)
[![Size](https://img.shields.io/badge/Host%20Binary-68%20KB-22c55e?style=flat-square)](https://github.com/Cooper-X-Oak/AgentIsland/releases)
[![Zero Electron](https://img.shields.io/badge/Electron-Zero-f59e0b?style=flat-square)](#why-directcomposition--webview2)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue?style=flat-square)](LICENSE)

<br />

<img src="docs/preview.png" alt="AgentIsland Split Pill, Approval Radar, Countdown & 4x Anti-Aliasing Microscope" width="100%" />

</div>

---

## Why AgentIsland?

If you run multiple AI coding agents (**Antigravity, Claude Code, Codex CLI, Cursor**) for 12–16 hours a day on Windows, two things quietly wreck your flow and health:

1. **Silent Agent Stalls**: You switch to your browser or another repo while an agent works — only to come back 15 minutes later and find it stuck waiting on an approval dialog or a question card.
2. **Lower-Back Destruction**: Deep flow means sitting for 3 hours straight without standing up once.

**AgentIsland** sits quietly at the top-center of your Windows 11 screen as an Apple-style **Split Pill**:
* **Left Pill — AI Agent Radar**: Live status of running agents, instant alerts for approvals / decisions / replies / errors, wait-time counter, recent history, and one-click jump straight back to the exact Antigravity conversation.
* **Right Ring — Posture & Break Coach**: Tracks real keyboard/mouse seated time via Win32 `GetLastInputInfo` (auto-resets if you lock your PC or step away ≥ 5 min), then expands with a guided countdown timer (`30s–120s`) and daily streak counter.

---

## Highlights

* **Zero Electron, Zero Jagged Edges, Zero Ghost Box**: Built in pure C# with `WS_EX_NOREDIRECTIONBITMAP` + **DirectComposition (`dcomp.dll`)** + system built-in **WebView2 CompositionController**.
  * 68 KB native host binary (~268 KB total portable zip).
  * Sub-pixel anti-aliased rounded corners & soft CSS shadows (`edgeMaxA = 0` on all 4 edges).
  * True cross-process mouse click-through (`WS_EX_LAYERED | WS_EX_TRANSPARENT` dynamically toggled every 30ms — never blocks browser tabs behind the transparent canvas or the 8px gap between split pills).
  * Fixed invisible canvas — **0 px window jump / flicker** during spring morph animations.
  * Automatically yields Z-order to Snipping Tool / screen capture overlays.
* **Low-Friction Interaction**:
  * **Hover (260ms)** to peek overview, move away (380ms) to auto-collapse; click or alert locks open.
  * **Right-click** the pill or ring for instant actions (Done, 30/45/50/60m interval, Mute, Pause 1h).
  * **Global Hotkeys**: `Alt + \` toggles the island; `Alt + Shift + \` jumps directly to the latest waiting Antigravity session.
* **Open, Zero-Port Event Bus**: Drop atomic JSON into `%LOCALAPPDATA%\AgentIsland\inbox\` or call `bin\island-push.exe` from any AI terminal, build script, or CI hook.
* **Built-in 22-Point Self-Verification Suite**: Run `.\verify.ps1 -Rebuild` to verify Hook contracts, DirectComposition HRESULTs, real `WindowFromPoint` click-through, per-pixel edge alpha, and generate a 4× anti-aliasing microscope sheet automatically.

---

## Quick Start (30 Seconds)

### Option A: Portable Release (Recommended, No Build Needed)
1. Download `AgentIsland-v0.1.0-win-x64.zip` from [Releases](https://github.com/Cooper-X-Oak/AgentIsland/releases) and extract it.
2. Double-click `bin\AgentIsland.exe` (or run `.\install-autostart.ps1` to add Start Menu & Windows Startup shortcuts).

### Option B: Build from Source (Zero SDK Install!)
Requires Windows 11 (or Windows 10 with Edge WebView2 Runtime). Uses Windows built-in .NET 4.x `csc.exe` and automatically downloads the official WebView2 NuGet DLLs on first run:

```powershell
git clone https://github.com/Cooper-X-Oak/AgentIsland.git
cd AgentIsland
.\build.ps1
.\start.ps1
```

Test both alerts immediately:
```powershell
.\test-push.ps1 -Mode both
```

---

## Connect Your AI Terminals

| AI Terminal | Status | Setup |
| :--- | :---: | :--- |
| **Google Antigravity** (Hooks + `transcript.jsonl` Approval Radar + `Ctrl+K` Exact Session Jump) | Live Verified | `.\adapters\antigravity\install.ps1` |
| **Claude Code** (`UserPromptSubmit` / `PermissionRequest` / `Notification` / `Stop` Hooks) | Contract Verified | `.\adapters\claude-code\install.ps1` |
| **Codex CLI** (`~/.codex/hooks.json` + legacy `notify` + `island-push.exe`) | Contract Verified | `.\adapters\codex-cli\install.ps1` |
| **Cursor / Windsurf / Cline / OpenCode** | Roadmap | PRs welcome! See [`CONTRIBUTING.md`](CONTRIBUTING.md) |

### Universal CLI Push Example
```powershell
.\bin\island-push.exe --source "Codex" --project "api-server" --title "Tests Passed" --body "All 42 unit tests green" --kind info
```

---

## Customize Break Actions & UI

* **Add your own break actions**: Edit [`config/actions.json`](config/actions.json) (change title, countdown seconds, tips, or add neck/eye exercises).
* **Tweak styling or animations**: Edit [`ui/index.html`](ui/index.html) directly.
* Right-click the tray icon → **Reload UI & Actions** — instant hot reload, zero recompilation.

---

## Architecture & Verification

```text
AI Terminals (Antigravity / Claude Code / Codex / CLI)
   └── island-hook.exe / island-push.exe  ──► %LOCALAPPDATA%\AgentIsland\inbox\*.json
                                                      │ (FileSystemWatcher)
Win32 GetLastInputInfo + Lock Detection ──────────────┼──► AgentIsland.exe (68 KB C# Host)
                                                      │     ├── Approval Radar (transcript tail)
                                                      │     ├── WS_EX_NOREDIRECTIONBITMAP + dcomp.dll
                                                      │     ├── Dynamic WS_EX_TRANSPARENT Click-Through
                                                      │     └── CoreWebView2CompositionController ──► ui/index.html
```

Run the automated full-stack verification suite anytime:
```powershell
.\verify.ps1 -Rebuild
```

---

## Roadmap

* [x] DirectComposition anti-aliased Split Pill + real cross-process click-through
* [x] Antigravity hooks + sandbox approval radar + `Ctrl+K` exact conversation jump
* [x] Claude Code hooks adapter + universal `island-push.exe` CLI
* [x] Externalized `config/actions.json` + guided countdown timer + daily streak
* [ ] Multi-monitor selector (choose primary / active cursor monitor)
* [ ] Native adapters for Cursor, Windsurf, Cline, and OpenCode
* [ ] Optional English / Chinese UI language toggle in settings

## License

[MIT](LICENSE)
