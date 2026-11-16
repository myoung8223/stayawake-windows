# Stay Awake: install commands

Quick reference. Run everything from an **admin** Command Prompt. Replace `StayAwake-1.0.3.msi` with your file name. The [README](README.md) has full details.

## Install

Defaults (10 min idle lock, 60 min max grace period, Ctrl+Alt+S, per-user history, everyone allowed):

```
msiexec /i StayAwake-1.0.3.msi /qn
```

With settings:

```
msiexec /i StayAwake-1.0.3.msi /qn IDLEMINUTES=10 MAXGRACE=60 HOTKEY="Ctrl+Alt+S" HISTORYSCOPE=User ALLOWEDGROUP="SCHOOL\Staff"
```

With a log, for troubleshooting:

```
msiexec /i StayAwake-1.0.3.msi /qn /l*v C:\Windows\Temp\StayAwake-install.log
```

`/qn` installs silently. Leave it off to see a progress window.

## Settings

| Property | Default | Values |
|---|---|---|
| `IDLEMINUTES` | `10` | Minutes idle before locking, 1–1440 |
| `MAXGRACE` | `60` | Longest grace period in minutes, 1–1440 |
| `HOTKEY` | `Ctrl+Alt+S` | e.g. `Win+Alt+K`, `Ctrl+Shift+F9`, or `None` |
| `HISTORYSCOPE` | `User` | `User` or `Machine` |
| `ALLOWEDGROUP` | *(everyone)* | Group(s) allowed to start grace periods, separated by `;` (e.g. `SCHOOL\Staff;Teachers`) |
| `IGNOREINJECTED` | `0` | `1` = ignore software-generated input (mouse jigglers) |
| `ARPSYSTEMCOMPONENT` | *(unset)* | `1` = hide from Apps & Features |
| `ARPNOREMOVE` | *(unset)* | `1` = remove the Uninstall button |

## Change settings without reinstalling

The service picks up changes within 10 seconds:

```
reg add HKLM\SOFTWARE\Policies\StayAwake /v IdleLockMinutes /t REG_DWORD /d 10 /f
reg add HKLM\SOFTWARE\Policies\StayAwake /v MaxGraceMinutes /t REG_DWORD /d 60 /f
reg add HKLM\SOFTWARE\Policies\StayAwake /v AllowedGroup /t REG_SZ /d "SCHOOL\Staff" /f
```

The other values are `Hotkey`, `HistoryScope`, and `IgnoreInjectedInput`. Hotkey and injected-input changes apply at the user's next sign-in.

## Upgrade

Install the newer MSI over the old one. It removes the old version automatically. Pass your settings again, because the install rewrites them and omitted ones go back to defaults:

```
msiexec /i StayAwake-1.0.4.msi /qn IDLEMINUTES=10 MAXGRACE=60
```

## Uninstall

```
msiexec /x StayAwake-1.0.3.msi /qn
```

Without the MSI file, in Windows PowerShell as admin:

```
Get-Package "Stay Awake" | Uninstall-Package
```

## Check that it's running

```
sc query StayAwakeService
type C:\ProgramData\StayAwake\service.log
```

The service should show `RUNNING`. Right-click the tray icon → **About Stay Awake** should show **Service: connected**.

## Emergency stop

If something misbehaves (for example, repeated locking), stop the service. The tray app exits with it:

```
sc stop StayAwakeService
```

## SCCM (ConfigMgr)

| Setting | Value |
|---|---|
| Install program | `msiexec /i "StayAwake-1.0.3.msi" /qn IDLEMINUTES=10 MAXGRACE=60` |
| Uninstall program | `msiexec /x {product code} /qn` (ConfigMgr reads the product code from the MSI) |
| Detection | MSI product code (filled in automatically) |
| Install behavior | Install for system |

For a new version, create a new application that supersedes the old one.
