# Architecture

A map of the code, for anyone who wants to change it. The app is small: about 4,400 lines of C#
and XAML in two projects.

## Overview

Edifier MR speakers have a Bluetooth LE control service, the one Edifier's ConneX phone app
uses. The app keeps one connection to it, shows the volume as a number in the taskbar, and sends
a command when you change something. Everything runs in one process, as the signed-in user. There
is no service, driver or background task.

```text
 Speakers  <-- Bluetooth LE -->  SpeakerConnection  -- events -->  App  -->  taskbar number
 (MR4.5,                         (Core, background                  |        panel, settings
  MR3, MR5)                       loop)                <-- requests -+
```

## Projects

| Project | What it holds |
| --- | --- |
| `src/EdifierMrVolume.Core` | The protocol and the connection. No UI, so it can be tested on its own. |
| `src/EdifierMrVolume` | The WinUI 3 app: taskbar number, menu, panel and settings. |
| `tests/EdifierMrVolume.Core.Tests` | Tests for Core, using bytes captured from real speakers. |

## Core

- **EdifierProtocol** builds and reads packets: frame, checksum, commands, and the service IDs of
  each model. Plain functions, no state.
- **SpeakerConnection** is one long-running loop. It finds the speaker by its advertisement,
  opens the control service, then sends queued writes and handles what the speaker pushes (every
  knob step and mode change). It asks the volume every 5 seconds to notice a dead link and the
  sound mode every 30 seconds. When the link drops it reconnects, waiting 2 to 30 seconds between
  tries. It raises `StateChanged` and `VolumeChanged` on its own thread.
- **EqPresets** and **RoomPresets** hold the equalizer presets, the named room-tuning choices and
  the volume-limit rule.

## App

- **App** starts everything: the connection, the taskbar icon (H.NotifyIcon) with its menu,
  sleep and wake handling, and the single-instance check. It moves the connection's events to the
  UI thread.
- **VolumeWindow** is the one window, with two views: the panel and the settings. Its code is split
  into partial files by area (panel, settings, equalizer, room, diagnostics).
- **TrayIconRenderer** draws the number at the taskbar's pixel size.
- **TrayWheel** handles the mouse wheel over the number; **OutsideClick** closes the panel on a
  click outside it.
- **Hotkeys** registers the keyboard shortcuts with Windows (RegisterHotKey) while the setting is
  on.
- **VolumeOsd** is the small volume bar shown over full-screen apps instead of the panel: a
  click-through window that never takes the focus, drawn like the tray number. It is not shown
  over a game in exclusive full screen.
- **PowerNotifications** turns the speakers off before sleep, if set, and reconnects after wake.
- **AppSettings**, **Log** and **Autostart** keep `settings.json` and `log.txt` in
  `%LOCALAPPDATA%\EdifierMrVolume` and the Start with Windows entry in the user's Run key.
- **ContainerEscape** restarts the app outside a packaged app's container, so its files land in
  the real `%LOCALAPPDATA%`.

## Threads

- The UI thread runs XAML and the taskbar icon's messages.
- The connection loop runs on a background task and never touches the UI.
- Two low-level mouse hooks each run on their own thread, and only while needed: the wheel hook
  while the pointer is over the number, the click hook while the panel is open.
- The keyboard shortcuts wait on their own thread while they are on. Windows wakes it only when
  one is pressed.

When the panel is closed, only the connection loop is active.

## Rules

- **Write only what the speaker reported.** A write changes one value and repeats the other bytes
  exactly as the speaker sent them. The sound-mode command carries six room-tuning bytes, and
  building them any other way once changed the room tuning.
- **One client at a time.** The speakers accept one Bluetooth control client. "Connect to speaker"
  off releases the link for the phone app.
- **Stay light.** The app runs all day. Check CPU use when changing the connection loop.
- **Core stays UI-free**, so it can be tested and reused.

## Build and release

The app is unpackaged and self-contained, compiled with [Native AOT](NATIVE-AOT.md).
`tools/package.ps1` builds the zip and the Inno Setup installer for x64 and ARM64; CI and the
release workflow are described in [RELEASING.md](RELEASING.md). The Bluetooth protocol is in
[PROTOCOL.md](PROTOCOL.md).
