# Privacy

Stay Awake for Windows does not transmit any data. It makes no network connections and has no telemetry, analytics, or update checks.

It stores the following locally on the device only:

- Admin settings, in `HKLM\SOFTWARE\Policies\StayAwake`.
- Each user's recent grace-period durations and end times, in that user's own registry profile (`HKCU\Software\StayAwake`). When an admin sets history to machine-wide, these are shared by everyone on the PC instead (`HKLM\SOFTWARE\StayAwake`).
- A service log at `C:\ProgramData\StayAwake\service.log` recording when the PC locked and why, and which Windows user started or stopped each grace period, with times. The log is capped at about 2 MB and older entries are discarded.

Uninstalling removes the settings, the shared history, and the log. Per-user recent values remain in each user profile until deleted.
