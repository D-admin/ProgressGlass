# ProgressGlass · Mint Desktop Companion

English | [中文](README.md)

A lightweight Windows desktop companion that shows local Codex chat activity, account usage snapshots, and connectivity checks through an animated chibi character.

![Animation poses and tray icon preview](preview-style.png)

## Features

- **Animated desktop pet:** mint-green hair and a pink dress, with blinking, gentle movement and waving. Hovering or clicking triggers a greeting.
- **Hover bubble:** reveal activity on hover, pin it with a click, drag the character to move it, and page through multiple chats.
- **Activity across local projects:** discover open Codex turns, pending tools and recognized approval events. Completed or interrupted turns disappear automatically.
- **Evidence-based status:** show recent events and silence without inventing completion percentages from milestones, elapsed time, tokens or animation.
- **Usage snapshots:** display remaining allowance, observation time and reset time from the newest available local Codex records. Older and expired snapshots are labeled.
- **Connectivity checks:** distinguish a verified public-resource response, browser challenges, HTTP failures and timeouts. A successful probe does not prove a model generation stream is healthy.
- **Matching head icons:** the tray, application window and executable use the mascot's head, with nine icon sizes from 16 to 256 pixels.
- **Optional measured progress:** record actual counts with units, sources and observation times. A legacy rectangular overlay and atomic progress updater are included.

## Download and run

1. Open this repository's **Releases** and download `ProgressGlass-v0.4.2-windows.zip`.
2. **Extract the whole ZIP** into a writable folder, such as a folder under Documents. Do not run it inside the archive.
3. Double-click `ProgressGlass.exe`. Keep the `assets` folder beside the executable.
4. The character appears near the bottom-right corner of the primary screen. Initial session scanning may take a few seconds.
5. Use Codex normally, then hover over the character to see the activity bubble.

Requires Windows 10/11 and .NET Framework 4.8 or a compatible runtime. This is a WinForms desktop program, not a browser extension; macOS and Linux are unsupported. The executable is not commercially code-signed. No service installation, administrator access, or API key is required.

The character can run without local Codex sessions, but activity and usage data may be unavailable.

## Controls

| Action | Result |
| --- | --- |
| Hover over the character | Open the activity bubble |
| Move outside both character and bubble | Close an unpinned bubble after about 0.65 seconds |
| Click the character | Pin the bubble; click again to close it |
| Drag the character | Move it and save its position |
| Pin / close button in the bubble | Pin or dismiss the bubble |
| Bottom arrows / mouse wheel | Page through activity |
| Right-click the character or tray icon | Change size, show the bubble, hide, or exit |
| `Ctrl+Alt+P` | Show / hide the character |
| `Ctrl+Alt+O` | Open and pin / close the bubble |

Choose 96, 128 or 160 logical pixels; Windows display scaling applies. Touch can use Windows mouse-promotion behavior, but physical touch-screen interaction has not been tested. Use the tray menu if a hotkey conflicts with another application. The current application UI is Chinese; this English guide documents its controls and behavior.

## Understanding the indicators

- **Recent activity:** a start, assistant output, reasoning metadata or tool event was written locally.
- **Waiting for tool output:** a call was recorded without a matching response yet.
- **Waiting / prolonged silence:** at least 30 / 120 seconds without a new recognized event. Silence cannot distinguish reasoning, network waiting and a stall.
- **Completed / interrupted turn:** removed from the active list. Ending a turn does not prove that the entire project is complete.
- **Connectivity probe passed:** `https://chatgpt.com/robots.txt` returned HTTP 200 with the expected content type and format. This does not inspect the generation stream.
- **Browser verification / HTTP error:** the probe was restricted. It does not by itself mean a chat disconnected, an account was blocked, or usage was exhausted.

Session updates run in the background roughly every second. New files and titles are discovered about every 10 seconds; the network probe runs every 30 seconds. Character animation is decorative, not a progress indicator or server heartbeat.

## Data coverage and privacy

The reader uses `$CODEX_HOME/sessions`, falling back to `%USERPROFILE%\.codex\sessions`. Chat titles come from `session_index.jsonl`.

Coverage is limited to user sessions in the current local directory. Other devices, cloud tasks and other account directories are not covered. Internal subagents and review sessions are excluded. Changes to Codex's local file format can affect compatibility.

The program reads event metadata and does not display or upload conversation text, reasoning bodies or tool contents. Its only proactive network traffic is an unauthenticated request to the public ChatGPT resource every 30 seconds.

Running it creates local files beside the executable:

- `pet-settings.json`: character position and size.
- `runtime.json`: diagnostics containing chat titles, paths, activity and usage. **Do not publish this file.**
- `settings.json`: preferences used by the legacy overlay.
- `usage-snapshot.json`: optional manually supplied usage snapshot; none is shipped.

The distribution excludes developer conversations, personal preferences, real session IDs, account usage and development-session records. `progress.json` and `examples` contain generic sample data only.

Usage typically updates only when a session records a new limit snapshot. Data older than five minutes is labeled; after the recorded reset time it becomes pending refresh rather than being assumed to reset to 100%. Old records cannot reliably distinguish switched accounts; defer to the client for authoritative allowance.

## Optional: record verifiable progress

Use `progress.json` to record the current action and evidence. When the total is unknown, update status without estimating a percentage.

```powershell
.\update-progress.ps1 -TaskId sample -Status doing -Evidence 'Verified result' -Current 'Current action'
```

When an actual count is available:

```powershell
# Example only: replace the counts and source with observed results.
.\update-progress.ps1 -TaskId sample -Status doing -MeasuredCompleted 240 -MeasuredTotal 1000 -Unit 'records' -Source 'worker-result.json'
```

Supported states are `todo / doing / blocked / review / done`. `done` requires verification evidence and, when measured, actual completion of the total. The updater never fills in the remaining count or averages task metrics into project completion.

A chat row can show project `current` and a separate project-level `measurement` when `monitor.sessionId` matches. `monitor.sessionPath` must identify the exact local JSONL file, and its `sessionId` must match the first `session_meta` record. The default template is not bound to any chat.

## Build from source

From Windows PowerShell 5.1+ or PowerShell 7 in the repository directory:

```powershell
.\build.ps1 -OutputDirectory .\dist
.\test.ps1 -ArtifactDirectory .\test-results
# Optional: regenerate the ICO after changing the head artwork
.\make-icon.ps1
```

The build uses Windows .NET Framework's bundled `csc.exe`; Node.js, Python, NuGet and paid generation services are unnecessary. Tests use local synthetic fixtures and the tracked `examples/blank-progress.json`. The build copies `assets` into the output directory and creates `progress.json` from that generic template only when it is absent, preserving existing records. Personal root-level `progress.json` is ignored by Git; a clean checkout can build and test without it.

Developer options: `--render-style preview.png` exports the mascot/icon preview; `--render-pet preview.png` exports a live bubble snapshot (reads local state and probes the network); `--legacy` opens the rectangular overlay; `--windowed` exposes a taskbar button for debugging.

## License and artwork

Code is licensed under the [Microsoft Reciprocal License (MS-RL)](LICENSE). Selected Win32 helpers come from [OnTopReplica](https://github.com/LorenzCK/OnTopReplica); see [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). Corresponding source is included in the distribution.

Artwork was created with AI assistance and is included with the software; see [artwork notice](assets/NOTICE.txt). This is an independent project with no official affiliation or endorsement from OpenAI or the upstream project.
