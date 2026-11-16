# Contributing

Bug reports, reports from other speaker models and pull requests are welcome.

## Reporting a problem

Use the **Bug report** form under Issues. Add the log if you can: Settings > Diagnostics > Open
log. The log contains your speaker's Bluetooth address; remove it if you prefer.

Have an MR3, MR5 or an 8625 DSP model? A short report saying what works and what doesn't helps
a lot, since only the MR4.5 has been tested.

## Building

You need Windows 10 or 11, the .NET 11 SDK and, for publishing with Native AOT, Visual Studio's
"Desktop development with C++" workload.

```powershell
dotnet test tests/EdifierMrVolume.Core.Tests
dotnet build src/EdifierMrVolume/EdifierMrVolume.csproj -p:Platform=x64
.\tools\install.ps1          # publish, install for the current user, start
```

Two things to know:

- Only one copy of the app runs at a time. Exit the installed one before starting a build.
- The speakers accept one Bluetooth client at a time. Exit the app before using
  `tools/probe.py`.

The code is laid out in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), the Bluetooth protocol
in [docs/PROTOCOL.md](docs/PROTOCOL.md).

## Pull requests

- Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/), because the
  release notes are built from them. Types and scopes: [docs/RELEASING.md](docs/RELEASING.md).
- Run the tests and `dotnet format EdifierMrVolume.slnx` before you push. CI checks both.
- Only send the speaker bytes it has reported itself, changing just the value you mean to change.
  A guessed byte can change settings you can't see, such as the room tuning.
- The app runs all day. Keep it light, and check CPU use when you change the connection loop.

By contributing you agree that your contribution is licensed under the [MIT licence](LICENSE).
