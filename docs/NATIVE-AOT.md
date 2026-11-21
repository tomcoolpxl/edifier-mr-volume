# Why Native AOT

Edifier MR Volume runs all day in the notification area, so it should start fast and use little
memory and CPU. In November 2026 we measured three builds of it on the same PC and picked
**Native AOT**.

## What Native AOT is

[Native AOT](https://learn.microsoft.com/dotnet/core/deploying/native-aot/) is Microsoft's way to
publish a .NET app as machine code. A normal .NET app ships its code as an intermediate language
that a just-in-time (JIT) compiler turns into machine code while the app runs. With Native AOT
that compiling happens once, when the app is built. Microsoft's own summary: Native AOT apps
"have faster startup time and smaller memory footprints" and run without the .NET runtime
installed.

It comes with limits. Code can't be generated or loaded at run time, and everything is trimmed
to what the app uses, so code that relies on reflection has to be written differently. For this
app that meant reading `settings.json` with the JSON source generator and a few changes for
C#/WinRT. Building it needs Visual Studio's **Desktop development with C++** workload, because the
native linker comes from there.

## The measurements

Measured 2026-11-16 on the owner's PC with an MR4.5. Each build was started fresh, left to
connect to the speaker, then left idle for 60 seconds.

| | .NET (JIT) | **Native AOT** | C++ prototype |
| --- | ---: | ---: | ---: |
| Start, until the volume number shows | 402 ms | **191 ms** | 137 ms |
| CPU used in 60 s idle | 109 ms | **31 ms** | 47 ms |
| Memory: working set | 117 MB | **76 MB** | 67 MB |
| Memory: private bytes | 72 MB | **64 MB** | 60 MB |
| Memory: Task Manager's "Memory" column | | **21.7 MB** | 18.7 MB |
| Threads | 31 | **22** | 19 |
| Install size | 237 MB, 536 files | **173 MB, 294 files** | 151 MB, 288 files |

- **Native AOT against the usual .NET build:** half the start time, less than a third of the
  CPU when idle, 41 MB less working set and 64 MB less on disk. Same code and the same features.
- **C++:** a first C++/WinRT build (WinUI 3, the same Windows App SDK) was made to compare. It
  had the notification-area number, the Bluetooth connection and the main panel, but not yet the
  settings, equalizer, room tuning or the other options. Even so, it saved only 3 to 9 MB of
  memory and about 50 ms of start time over Native AOT. An empty C++ WinUI window alone already uses 80 MB of
  working set, so most of the memory is WinUI itself, whatever language uses it. Rewriting the
  whole app in C++ wasn't worth that, and the experiment was dropped.
- CPU at this level is mostly noise: 31 ms in a minute is about 0.05 % of one core, and the C++
  build's 47 ms isn't really worse.

## Smaller download

The Windows App SDK package brings all its components, and the app uses only WinUI. After 1.2.1
the csproj leaves out machine learning (ONNX Runtime, DirectML), AI, Search, Widgets and the
framework-runtime package with `ExcludeAssets="all"`, as a Windows App SDK maintainer advised in
[microsoft/WindowsAppSDK#5969](https://github.com/microsoft/WindowsAppSDK/issues/5969):

| | 1.2.1 | After |
| --- | ---: | ---: |
| Installed | 128.5 MB | 70.9 MB |
| Zip | 51 MB | 28 MB |
| Installer | 35 MB | 19 MB |

Idle CPU stayed the same: 0 to 31 ms in a minute, 23 to 25 threads, 78 MB working set.

## How it is kept

- `<PublishAot>true</PublishAot>` is set in `src/EdifierMrVolume/EdifierMrVolume.csproj`, so
  every publish (`tools/install.ps1`, `tools/package.ps1`, CI and releases) is Native AOT.
- The build has no trimming or AOT warnings left; new ones are worth fixing, not hiding, because
  they point at code that may fail only in the published app.
- CI starts the published app and fails if it ships the .NET runtime (`coreclr.dll`), so a
  build can't fall back to JIT unnoticed. The ARM64 build is checked the same way on a Windows on
  Arm runner.
