# Light Weight Syslog

Light Weight Syslog is a small WinForms syslog receiver for ad-hoc diagnostics on remote Windows PCs.

## Highlights

- Targets **.NET Framework 4.8** for Windows 11 and Windows Server 2022 compatibility
- Receives **UDP syslog** on a configurable port with a quick switch to **514**
- Parses **RFC 3164** and **RFC 5424** style messages
- Shows **source IP**, host, severity, facility, app, sender time, and message text
- Includes **text**, **severity**, and **source IP** filters
- Supports **pause/resume**, **auto-scroll**, and a parsed/raw detail pane
- Exports the current view to **CSV** or **text**
- Automatically writes every received message to a **session log file** in the startup directory

## Run

1. Open `Run-LightWeightSyslog.cmd`
2. Or launch `bin\Release\net48\LightWeightSyslog.exe`

## Session log files

Each run creates a log file in the directory the app was started from:

`LightWeightSyslog-YYYYMMDD-HHMMSS.log`

The UI also includes an **Open log file** button for the current session log.
