# TODO

Open requests, in the owner's words where possible. Tick an item only when it is done and
verified, with a note saying how. Before any question to the owner, every unticked item here is
either offered or named as deliberately left out.

## Done

- [x] **App icon**: flat, abstract, vector line-style, in the Edifier MR4.5's own look and colours,
      without the Edifier name. Not in the taskbar (the tray keeps the volume number); shown in
      the settings/About part of the panel. *Done: `assets/icon/*.svg` (graphite cabinet, warm-white
      tweeter, copper woofer), exe icon via `tools/make-icon.ps1`, About row in settings.*
- [x] **Firmware check**: the speaker reports 1.0.0 (`0xC6` → `01 00 00`). *Checked: omarchy-edifier-mr3
      reads the same command the same way (bytes joined by dots) and gets 1.0.7 from an MR3, so this
      is the firmware version; 1.0.0 fits a model released in August 2026. The ConneX app can confirm.*
- [x] **Installer**: per-user `setup.exe` (Inno Setup, like cash) attached to each release.
      *Done: `packaging/inno`, built by `tools/package.ps1`; tested locally (silent install,
      files, Installed-apps entry, uninstall) and in CI on every push (green). First release with
      it: the next one.*
- [x] **One-click release**: Actions › Release › Run workflow, pick patch/minor/major; it raises the
      version, commits `chore(release): X.Y.Z`, tags, builds and publishes ("go up one").
      *Done in `release.yml` + `prepare-release.ps1`; bump and changelog dry-run locally. Not run
      on GitHub yet, because running it publishes a release.*
- [x] **Release notes**: the GitHub issues closed since the previous release, and the notes
      committed to `CHANGELOG.md`. *Done; CHANGELOG.md starts with v1.0.0.*
- [x] **Feature ideas list** for the owner to pick from. *Written and picked from (below).*
- [x] **Full UX review** ("Do UX review. Think. Check online."). *Written with sources (below);
      the owner picked all four fixes.*
- [x] **The 14 GitHub issues I wrongly created**: *closed, then permanently deleted on the
      owner's choice (2026-11-16), with the labels made for them.*

## To build: picked by the owner

UX fixes:
- [x] Panel sizes to its content (text scaling). *Done: measures its content when shown and when
      it changes; tested with the first-run hint and the settings view.*
- [ ] Say why it can't connect (reason in the panel; hint after ~15 s of searching). *Built (reason
      line under the header, hint after 15 s, "Released…" text); not yet seen on screen: needs the
      speaker off or the link released.*
- [ ] Panel next to the taskbar wherever it is. *Built (bottom/top/left/right from the work area);
      only the bottom taskbar tested, the only layout on this PC.*
- [ ] Wording ("notification area"; menu "Show volume" → "Open"). *Done in the app, installer and
      shortcut; README still to do.*

Speaker:
- [x] Sound mode: show and switch Monitor / Music / Custom (test switching on the speaker first).
      *Done: SelectorBar under the slider (WinUI's control for 2-3 modes); switched Monitor → Music →
      Monitor from the panel on 2026-11-16, the bar followed the speaker's reply, and the room
      tuning read back unchanged afterwards (`00 02 00 1E 03 00 01`).*
- [ ] Input: show and switch (find out which number is which input first). *Owner (2026-11-16): "i
      thought it mixed the signals? check online first". Checked: the MR4.5 manual says all audio
      inputs, Bluetooth included, can be used in parallel, so there is no input to switch; what
      `0x61` ("1 of 4") means is unknown. Owner, 2026-11-16: investigate further (read `0x61` while the owner plugs and unplugs sources and connects Bluetooth).*
- [x] Volume limit (setting; caps what the app sets, not the knob). *Tested on the speaker: limit 6,
      slider asked for 8, speaker went to 6. Switched back off.*
- [ ] Power-off: **a right-click menu item and a button in the panel, no confirmation** (owner's
      choice, 2026-11-16). *Built 2026-11-16: power button left of the gear and "Switch speakers
      off" in the right-click menu, both enabled only while connected; after a power-off the
      panel says "Switched off. Switch the speakers on with their power button; the app
      reconnects by itself." Checked: the button shows and is enabled when connected. Not yet
      pressed in the app (it switches the speakers off; the command itself was tested with the
      probe). The owner can try it any time.*
- [ ] **Switch the speakers off when the PC goes to sleep** (owner's own idea; setting, off by
      default). *Built (setting in Power; Windows' suspend callback sends the power-off); must be
      tested with a real sleep.*
- [x] **Equalizer**, in settings as an expander (owner's choice, 2026-11-16), not in the main
      panel. *Done 2026-11-16: Settings › Sound › Equalizer: preset ComboBox (Deep bass, Balanced,
      Deep + balanced, Custom) and 9 horizontal band sliders (horizontal because the page scrolls
      vertically, per Microsoft's slider guidance), steps −6 to +6. Choosing a preset or moving a
      band switches the speakers to Custom mode; moving a band makes it the Custom curve, which
      the app keeps (the speaker has one custom curve). The app reads the curve once per
      connection (no polling) and writes only the bands that change, each after the previous
      ack. Tested on the MR4.5 through UI Automation: Balanced (1 band, 0.26 s), Custom (the
      owner's +4/+5/+4/+2, 5 bands), Deep + balanced (5 bands); the panel showed the speaker's
      read-back each time and the owner's Custom curve stayed intact.* *Writing one band tested and restored (see protocol facts).*
- [x] **Equalizer presets** (owner, 2026-11-16): "create 2 or 3 presets, and because the bass is
      not good, and there's already a sound profile, but I want something with proper bass, but I
      don't know how to do that. So 2 or 3 presets for the user to choose from, plus custom."
      Research how to shape bass for a 4.5" monitor first (sources), propose the curves, let the
      owner choose. Owner, 2026-11-16, on the proposal (Deep bass 62 +3 / 125 −1; Balanced 125 −2 /
      2k +2; Deep + balanced): **listen first**, one preset at a time on the speaker, then restore
      the owner's curve. **Owner, 2026-11-16: "Check the exact specs of the speakers. I did not change
      the two dials at the back, and then check what people say in reviews or measurements, and
      then make the profiles. You're just guessing now."** So: official specs (drivers, response,
      crossover, rear High/Low dials and what they do at their default), the sound modes, the
      ConneX EQ bands and step size, then reviews and measurements; curves only from that, with
      the source for each choice. (The first proposal, made before this, was partly guessed.)
      *Listened 2026-11-16, each preset written by `tools/eq_audition.py` in Custom mode and read
      back. **Owner's choice: all three** — Deep bass (62 Hz +3, 125 Hz −1), Balanced (125 Hz −2,
      2 kHz +2), Deep + balanced (62 Hz +3, 125 Hz −2, 2 kHz +2) — plus Custom. Deep + balanced was
      left on the speaker as its Custom curve (owner's choice); the owner's previous curve (+4/+5/+4/+2
      at 62/125/250/500 Hz) is kept in `~/.edifier-eq-backup.json` and can be put back with
      `eq_audition.py restore`. Built in the app (see Equalizer); the owner's previous curve is
      the app's Custom curve.*
- [ ] **Auto-standby**: investigate the protocol. *2026-11-16: `0xD3` reads a timer (`00 00` =
      off); `0xD1 00 01` set it to 1 minute (read back `00 01`), `0xD2` switched it off again (read
      back `00 00`). Owner asked why 20 minutes: it was an arbitrary example; the test used 1
      minute. Still unknown: whether it is a shutdown timer or auto-standby on silence.
      Owner, 2026-11-16: find out with a 1-minute test while music plays.*

Convenience:
- [x] Mute (click the speaker symbol; click again restores). *Tested: 7 → 0 → 7.*
- [x] Start volume (setting; set a level when the app connects; off by default). *Tested: start 5,
      Reconnect, the app set 6 → 5 on connecting. Switched back off.*
- [ ] **Scroll wheel over the notification-area number changes the volume, one step per notch**
      (owner, 2026-11-16: "If I hover over the taskbar thing, the scroll wheel of the mouse should
      control the volume one by one. Be careful that I don't, you know. But there is a limit.").
      One step per wheel notch, never more; the Volume limit applies. Check online how Windows
      apps do this and keep it light (the app runs all the time); measure CPU after.
      *Built 2026-11-16 the way VolumeScroller does it (low-level mouse hook +
      Shell_NotifyIconGetRect), but the hook exists only while the pointer is over the number:
      the icon's own pointer-move report installs it, leaving the icon removes it. A notch up
      never lowers the volume (when the knob has it above the limit); touchpad scrolls add up to
      a full notch. Checked: hovering installs the hook with the icon's exact position. Not yet
      checked: a real scroll changing the volume (my injected wheel events were dropped by the
      sandbox or landed while the owner moved the mouse). The owner tries it by hand.*
- [x] First-run hint (where to switch the notification-area icon on; once). *Tested: shown on the
      first start of the new build, then marked done in settings.json.*

Reviews (owner, 2026-11-16: "make sure it doesn't steal too many cycles", "Do UX review. Think.
Check online", "check online how to do UX properly according to the Windows ... and WinUI 3
guidelines"):
- [x] **Lightweight check**, re-measured on the current build and written down here (the first
      measurement, 0.02 % CPU while connected, was only said in chat). *2026-11-16, connected and
      idle for 60 s: 219 ms CPU = 0.37 % of one core = 0.02 % of the 16-core PC; 106 MB private
      memory (173 MB working set), 54 threads; Bluetooth: one 6-byte query every 0.5 s. Same as the
      first measurement. Light enough to leave as is.*
- [x] Lightweight check again after the speaker features change the connection loop. *2026-11-16,
      with the sound mode read every 5 s: 312 ms CPU per 60 s = 0.52 % of one core = 0.03 % of the
      PC; 73 MB private memory, 30 threads. Still light.*
- [x] Lightweight check after the equalizer, power-off and wheel work. *2026-11-16: connected
      and idle, 48 ms CPU in 30 s (about 0.1 s a minute, 0.01 % of the PC), lower than before
      (312 ms a minute). Two earlier windows measured 1.2 and 2.3 s a minute while the owner used
      the panel: the busy thread started with the panel window at 01:17:05. The wheel hook's
      thread was gone after the pointer left the number (no thread from 01:17:04 alive).*
- [x] **Settings per Microsoft's WinUI guidance**: SettingsCard / SettingsExpander from the Windows
      Community Toolkit, BodyStrong section headers, disabled cards say why, About as an expander
      at the bottom with name, icon, version and links
      ([Guidelines for app settings](https://learn.microsoft.com/windows/apps/design/app-settings/guidelines-for-app-settings)).
      *Done: sections Panel, Volume, Connection, About; Diagnostics and About are expanders.*
- [x] Check Microsoft's WinUI design skill (microsoft/winappCli `winui-design`) for anything else.
      *Read. Findings: (1) it suggests TeachingTip for onboarding hints; the first-run hint stays an
      InfoBar because a TeachingTip can't point at the notification-area icon, which is outside the
      app. (2) It says ComboBox for 4+ choices, but Microsoft's settings guidelines allow radio
      buttons up to 5, so "Close after" keeps them. (3) Destructive actions need a confirmation:
      applies to the power-off button, a question for the owner. (4) Test in a High Contrast
      theme: added to Verify.*
- [ ] **Full UX and UI review, after all the UI work is done** (owner, 2026-11-16: "I want you to
      do a UX and UI review with whatever skill is needed ... I tried to plan this before with you,
      and you disregarded my command ... When ... you're done with the UI work, I want you to do
      this. Make a note of this"). Use Microsoft's `winui` plugin skills (`winui-design`,
      `winui-code-review`, `winui-ui-testing`; installed 2026-11-16, user scope) plus the
      design-critique, accessibility-review and ux-copy skills and Microsoft's app-settings
      guidelines; cover the tray icon, tooltip, menu, panel, settings, equalizer and every text;
      report findings with sources before changing anything.
- [ ] Decide whether to install Microsoft's WinApp CLI, which `winui-design` uses for its sample
      search (`winapp find-ui`); the skill works without it.
- [x] **Rescan**: never offered. Reconnect already rescans before connecting; tell the owner and let
      them decide whether a separate Rescan is wanted. *Owner, 2026-11-16: "No, Reconnect is
      enough".*

Project:
- [x] Make the repo public, then add a Scoop manifest to tomcoolpxl/scoop-bucket (like cash's)
      *Done 2026-11-16: public; manifest tested with a real scoop install/uninstall and pushed.*
- [x] About links: GitHub repository and "Report a problem". *Done as clickable cards in the About
      expander; not clicked in a test (they open the browser).*
- [ ] Dependabot for NuGet packages and GitHub Actions
- [ ] README, screenshots (main panel, settings) and commits for all of the above
- [ ] **Verify**: phone-app handover, wake from sleep, a real reboot with Start with Windows, the
      release zip in a clean folder; light theme and high contrast

## Not picked (2026-11-16)

Offered and not chosen; listed so they don't silently disappear:
- Keyboard shortcuts for the speaker volume
- winget package (needs the installer submitted publicly to microsoft/winget-pkgs)
- Label-based pull-request sections in the release notes
- MSIX package assets

## Notes from the UX review (2026-11-15)

- **Fixed panel heights clip with larger text.** Windows' "Text size" scales WinUI text; Microsoft's
  guidance says containers must resize and reflow
  ([text scaling](https://learn.microsoft.com/windows/apps/develop/input/text-scaling)).
- **Silent failure while searching.** When the phone app holds the speaker, or Bluetooth is off,
  the panel only says "Looking for speaker…" / "Retrying…" and hides the reason in a tooltip.
- **Panel always bottom-right**, covering a taskbar on the top or a side (Windows 10).
- **Wording.** Microsoft's style guide says "notification area", not "system tray"
  ([style guide](https://learn.microsoft.com/style-guide/a-z-word-list-term-collections/n/notification-area)).
- Fine as is: the icon shows status (what the notification area is for, per
  [Microsoft's guidelines](https://learn.microsoft.com/windows/win32/uxguide/winenv-notification));
  settings in the right-click menu; nothing optional on by default; the menu closes properly;
  controls have accessible names.

## Protocol facts for the speaker features (MR4.5, 2026-11-15)

- Speaker facts for the EQ presets (2026-11-16):
  4.5" mid-bass driver (30 W) + 1" silk tweeter (10 W), crossover 2800 Hz, 50 Hz-40 kHz
  ([Edifier](https://www.edifier.com/global/p/studio-monitors/mr4-5)). Rear knobs (manual): High
  ±6 dB above 10 kHz, Low ±6 dB around 125 Hz, 0 at the centre; the owner hasn't touched them.
  Modes (manual): red = Monitor, green = Music, white = Custom (the 9-band EQ); an MR4 review
  describes Music as warmer, more mid-bass and lower treble; Edifier doesn't publish the curves.
  Measured ([Erin](https://www.erinsaudiocorner.com/loudspeakers/edifier_mr4_5/)): −3 dB at 60 Hz,
  −10 dB at 44 Hz, upper-bass emphasis 120-150 Hz, recession 2-3 kHz, compression from 96 dB
  mostly in the bass. Heard ([ecoustics](https://www.ecoustics.com/reviews/edifier-mr4-5/)): rolls
  off around 45 Hz, mid-bass hump around 125 Hz, would like more 10-80 Hz and 2-3 kHz.
  EQ bands 62/125/250/500 Hz/1/2/4/8/16 kHz, gain byte 0-12 with 6 = flat (steps, unit not published: Edifier's EQ
  pages, the reviews and the app store pages don't give it; the app shows steps, not dB;
  not confirmed). The owner's speaker now: Custom curve "Sound Effects" = +4/+5/+4/+2 at
  62/125/250/500 Hz, which boosts exactly the measured hump; room settings low cut 30 Hz
  −24 dB/oct, acoustic space 0 dB, desktop control on.
- Sound mode `0xD5` → `00 02 00 1E 03 00 01`: mode 0 (Monitor), index 2, low cut 30 Hz,
  slope −24 dB/oct, acoustic space 0 dB, desktop on. Modes: 0 Monitor, 1 Music, 2 Custom.
  **Switching tested 2026-11-16**: `0xC4` + all 7 bytes with only the first replaced
  (`AA 0C C4 00 07 01 02 00 1E 03 00 01 A6` = Music); the speaker acks `01` and pushes `0xD5`.
  *Incident:* a first try sent 6 bytes by mistake; the speaker read the checksum as the 7th byte
  and changed its room tuning (low cut 3, slope −6, space −1 dB, desktop invalid) for about two
  minutes, until the original 7 bytes were written back and read back identical. The app must
  only ever echo the bytes the speaker reported, never typed ones.
- Input `0x61` → `04 01`: input 1 of 4; set with `0x62`; mapping unknown.
- Power off `0xCE`: works; no reply.
- Custom EQ `0x43`: 9 bands `[index][freqHi][freqLo][gain]`, gain 6 = flat. **Writing tested
  2026-11-16**: `0x44` + `[01][band][freqHi][freqLo][gain]` (5 bytes) raised 62 Hz from 10 to 11,
  acked `01`, read back 11; then set back to 10 and read back identical to before.
- Auto-standby `0xD7`: no reply. `0xD8`: 29-byte feature table, not decoded. **`0xD3` answers
  `00 00`** (2026-11-16): in mEDIFIER (Edifier headphones) `0xD3` is the auto shutdown timer,
  set with `0xD1 [00][minutes]` and switched off with `0xD2`; on the MR4.5 setting is untested.
