# 0051: Control the HTTP server independently from the tray

## Status

Accepted, 2026-11-26.

## Context

The user requested a Server submenu with a green/dark-gray status LED and Start/Stop commands. Previously the tray exited whenever the HTTP host stopped, which prevented starting the server again from the tray.

## Decision

Keep the tray process alive when its Server > Stop command cancels the HTTP host. A controller serializes Stopped, Starting, Running and Stopping states. Only the runtime readiness callback sets Running and the green LED. Start is enabled only while stopped, Stop while starting or running, and both are disabled during shutdown. Startup failure returns to Stopped and is reported through the existing error dialog, allowing retry.

Keep the existing per-profile host lock for the entire tray process lifetime, including while HTTP is stopped. Each subsequent server run uses that owned lock. Direct runtime callers continue to acquire their own lock. Server restarts create fresh runtime services and connection credentials; shutdown revokes discovery before Start is enabled again. Readiness callbacks from a prior run cannot mark a newer run ready.

Exit Githubie, session ending, restart and updater installation still end the whole process. A normal external control-pipe stop also exits the process, preserving CLI and installer behavior while the server is running. While the server is stopped from the tray, use that tray's Start or Exit command; the HTTP discovery/control endpoint is unavailable and a second process cannot acquire the profile lock.

The approved white H icon is embedded in executables and desktop assemblies. Runtime icons do not use external paths. The installed-app listing uses the same ICO.

## Alternatives

- Exit the tray on every stop: cannot satisfy tray-based Start after Stop.
- Start a separate server process: adds process control and IPC without needing a separate trust boundary.

## Impact and security

Authentication, owner checks, canonical configuration paths and repository policy remain in HostRuntime. Stop releases runtime resources but retains exclusive ownership of the profile until the tray exits. No new remote start/stop API or authorization bypass is introduced.

## Implementation and validation

UserHostServerController manages state, UserHostApplication manages process lifetime and the profile lock, and UserHostTrayContext presents localized commands and LEDs. Controller tests cover repeat starts, cancellation during startup/shutdown, stale readiness, failure, restart and process-exit cancellation. Validate packaged icon resources, localized menu states and repeated real HTTP start/stop cycles. USER_HOST guides document the menu and stopped-server behavior.
