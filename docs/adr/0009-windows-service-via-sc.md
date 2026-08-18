# ADR 0009: Windows Service management via fixed `sc.exe` invocation

- Status: Accepted

## Context

`Githubie.Server.exe` should be able to run as an unattended, auto-starting Windows Service, and the management CLI needs to install/start/stop/query it without requiring a separate service-management dependency.

## Decision

`Githubie.Server` hosts itself via `Microsoft.Extensions.Hosting.WindowsServices` (`AddWindowsService(service => service.ServiceName = "Githubie")`), so the same executable runs both interactively and as a service. `Githubie.Cli.WindowsServiceManager` controls the service lifecycle by invoking `%SystemRoot%\System32\sc.exe` as a subprocess with a fixed argument list (`create`, `delete`, `start`, `stop`, `query`) rather than calling the Windows Service Control Manager API or WMI directly. The service name is the fixed constant `"Githubie"`.

## Alternatives

- `System.ServiceProcess.ServiceController` / native SCM API calls: not selected for Phase 1; `sc.exe` with a fixed command grammar is simpler to test (`IServiceCommandExecutor` is mockable) and matches Buckettie's proven approach.
- WMI: rejected as unnecessary additional surface area for a fixed, small set of operations.

## Impact

`service install` always registers with `start= auto` and the fixed display name `"Githubie MCP Server"`; there is no per-deployment customization of the service name in Phase 1.

## Security conditions

`sc create`'s `binPath=` is built from the trusted, locally-resolved Server executable path and the `--config` path supplied to the CLI, not from untrusted input.

## Operational conditions

`service install` and Windows Service start/stop typically require Administrator privileges; the CLI does not elevate itself.

## Implementation, tests, and documentation

`Githubie.Cli.WindowsServiceManagement` (`IServiceCommandExecutor`, `ScServiceCommandExecutor`, `WindowsServiceManager`). Not exercised against a real Windows Service registration during Phase 1 real-machine verification (deferred pending explicit authorization, since it modifies system service state); `Githubie.Server.exe` itself was verified running interactively.
