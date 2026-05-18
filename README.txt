Light Weight Syslog
===================

What it is
----------
A small Windows Forms syslog receiver for ad-hoc diagnostics on remote PCs.
It targets .NET Framework 4.8 so it can run on Windows 11 and Windows Server 2022
without installing a newer .NET runtime.

Current features
----------------
- UDP syslog listener with configurable port
- Quick switch to standard syslog port 514
- RFC 5424 and RFC 3164-style parsing
- Live message grid with severity/facility decoding
- Text filter, severity filter, and source IP filter
- Pause/resume display without stopping capture
- Auto-scroll toggle
- Automatic append-to-file session logging
- Open log file button
- Detail pane showing parsed and raw message content
- Export current view to CSV or text
- Copy target endpoint and copy selected message details

Default port
------------
The app defaults to UDP 5514 for easier ad-hoc use, but you can click "Use 514"
or type any port you want before starting the listener.

How to run
----------
1. Double-click Run-LightWeightSyslog.cmd
2. Or run:
   bin\Release\net48\LightWeightSyslog.exe

Automatic log file
------------------
The app automatically writes every received message to a session log file in the
directory it was started from. The filename format is:

  LightWeightSyslog-YYYYMMDD-HHMMSS.log

Use the "Open log file" button in the app to open the current session log.

Build output
------------
- bin\Release\net48\LightWeightSyslog.exe
- bin\Release\net48\LightWeightSyslog.exe.config

Notes
-----
- If a device is configured to send to UDP 514 and Windows blocks inbound traffic,
  allow the port through Windows Firewall.
- The app keeps an in-memory rolling buffer and trims old messages after 20,000 entries.
- Trimming only affects the on-screen buffer. Messages already written to the session
  log file are preserved.
