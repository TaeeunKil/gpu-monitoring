# Verification

2026-11-16, Windows x64, .NET 8 SDK, Microsoft.Data.Sqlite 8.0.34.

## Behavioral checks

The console test runner checks 18 cases using temporary SQLite files and an ephemeral loopback HTTP server:

- missing readings versus measured zero, numeric strings and invalid values;
- offline readings and historical/inventory values;
- independent VRAM and compute metrics;
- shared-process ownership, unattributed usage and inconsistent totals;
- malformed responses and unsupported metrics;
- SQLite null/zero persistence, source separation and snapshot deduplication;
- threshold entry, recovery, repeated high readings and missing readings;
- bound SQL parameters and bounded history retention;
- state GET and explicit refresh POST/header;
- non-loopback URLs, embedded credentials, HTTP errors and redirects.

## Native UI smoke

The published EXE supports `--smoke-test <folder>`. It uses fabricated data and a separate SQLite database to check initial server/GPU selection, unknown versus zero, threshold alarms, history, offline state, a smaller window, an unavailable local endpoint and return to demo mode. It saves `result.json` and five previews before closing.

Native Windows controls were also checked interactively. `DrawToBitmap` previews may omit native combo-box selection text, so the actual controls were verified separately.

## Limits

This project has not been validated against a production GPU collector, remote SSH host or physical GPU during this verification. Long-running operation, load testing, all display scales and independent code review are outside this check. The test results describe the C# client, not any separately operated collector.
