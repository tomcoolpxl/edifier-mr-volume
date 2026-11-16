# Edifier MR Volume

Unofficial Windows tray app (WinUI 3, .NET 10, Windows App SDK 2.6, unpackaged and
self-contained) that shows and sets the hardware volume of Edifier MR studio monitors over
Bluetooth LE. See README.md for features and the verified protocol.

## Rules

- Don't add features on your own initiative. Propose them and let the owner pick first.
- Commit subjects follow Conventional Commits (`feat(scope): ...`, `fix(scope): ...`); the
  release notes are generated from them. See docs/RELEASING.md for types, scopes and the
  release routine (version lives only in Directory.Build.props).
- Keep the app light: it runs all the time. Measure CPU before and after changes to the
  connection loop (`SpeakerConnection.PumpAsync`).
- Only one BLE client can hold the speaker's control link. Exit the tray app (or untick
  "Connected to speaker") before running `tools/probe.py`.

## Commands

```powershell
dotnet test tests/EdifierMrVolume.Core.Tests                      # protocol tests
dotnet build src/EdifierMrVolume/EdifierMrVolume.csproj -p:Platform=x64
.\tools\install.ps1                                                # publish + install + start
pwsh .github/scripts/release-notes.ps1 -Tag vNEXT                  # preview release notes
```
