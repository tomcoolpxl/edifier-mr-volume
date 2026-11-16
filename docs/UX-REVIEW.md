# UX and UI review: Edifier MR Volume

Review date: 2026-11-16. Covers the code at commit `ebff015`. This is a report only: nothing in the app
was changed. The app was not run or touched while writing it.

**How it was done.** I read the XAML, the code-behind, the tray, wheel and icon code, the installer
texts and README, and the owner's decisions in TODO.md. I looked at the screenshots listed under
"What I could not check" and compared everything with Microsoft's current guidance on Microsoft Learn.
Every source is linked where it is used. I followed these skills: `winui-design`, `winui-code-review`,
`design-critique`, `accessibility-review` and `ux-copy`. WinApp CLI is not installed, so I skipped the
`winapp find-ui` and `winapp find-api` steps. API claims were checked against Microsoft Learn and the
package sources instead (H.NotifyIcon 2.4.1 and the Community Toolkit on GitHub).

**Assumptions.**
- Users: the owner, plus anyone on GitHub with an MR4.5, MR3 or MR5, on Windows 10 2004+ or
  Windows 11, in English.
- High means a group of users is blocked, or the speakers do something the user didn't want.
- Medium means many users get confused or lose something, or a guideline is broken with a real
  effect.
- Low means polish.
- Ideas that would add something new are marked **proposal**. Per CLAUDE.md, the owner picks those.

## 1. Summary

The app is in good shape for a small notification-area utility:
- It does what Microsoft's notification-area guidance asks for: the icon shows status, a click opens
  a light-dismiss panel next to the icon, and changes take effect immediately.
- The settings view follows the WinUI app-settings guidelines almost to the letter.
- The XAML uses theme brushes and text styles throughout.

The biggest problems:
- **Keyboard users.** Keyboard users can't operate the notification-area icon at all: Enter and
  Shift+F10 do nothing. Three commands exist only in its right-click menu.
- **Focus.** Keyboard focus is lost every time the panel switches between the volume view and the
  settings view.
- **Power button.** The power button is small and sits right next to the gear. One slip switches the
  speakers off, and they can only be switched back on at the speakers.
- **Equalizer.** "Custom" means two different things, and nudging one band of a preset silently
  overwrites the saved Custom curve.
- **Status and contrast.** Connection problems appear as the smallest grey text in the panel. The
  notification-area digits don't follow contrast themes.

Most fixes are small: a focus call, an event handler, an InfoBar, a rename.

## 2. Findings, most important first

### 2.1 The notification-area icon can't be used with the keyboard

- **Where:** notification-area icon and its menu. `src/EdifierMrVolume/App.xaml.cs:132-140` (only
  `LeftClickCommand` and `ContextFlyout` are wired) and `App.xaml.cs:115-127` (the menu).
- **What is wrong:**
  - Windows lets users reach notification-area icons with the keyboard: Win+B, the arrow keys, then
    Enter or Space to activate, or Shift+F10 / the Menu key for the menu. Windows then sends
    `NIN_KEYSELECT` or `WM_CONTEXTMENU`.
  - H.NotifyIcon 2.4.1 only raises the routed events `TrayKeyboardKeySelect` and
    `TrayKeyboardContextMenu` for these (`TaskbarIcon.KeyboardEvents.cs`, `MessageWindow.cs`, tag
    v2.4.1). It runs `LeftClickCommand` and opens the menu only on mouse button-up. The app handles
    neither event.
  - So, from the source, Enter does nothing and the menu can't be opened from the keyboard.
  - **Start with Windows**, **Connected to speaker** and **Exit** exist only in that menu.
    Keyboard-only and Narrator users can open the panel only by starting the app again from Start,
    which nothing in the app tells them. They can never reach those three commands.
- **Guidelines:**
  - [WCAG 2.1.1 Keyboard](https://www.w3.org/WAI/WCAG21/Understanding/keyboard.html).
  - [Shell_NotifyIcon remarks](https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw)
    (`NIN_KEYSELECT` for Space/Enter, `WM_CONTEXTMENU` for the keyboard menu).
  - [Keyboard accessibility](https://learn.microsoft.com/windows/apps/design/accessibility/keyboard-accessibility)
    says "Any UI that can be activated by pointer should also be invokable by keyboard".
  - [Menus](https://learn.microsoft.com/windows/win32/uxguide/cmd-menus) says "Don't make commands
    only available through context menus".
- **Fix:**
  - Handle `TrayKeyboardKeySelect` and call `ToggleWindow`. Open the panel with
    `FocusState.Keyboard` so the slider shows a focus rectangle.
  - `WM_CONTEXTMENU` also follows a mouse right-click, as H.NotifyIcon's own comment says, so opening
    the menu from it could show the menu twice.
  - The simpler route for the menu commands: also offer **Start with Windows** and **Connected to
    speaker** as switches in Settings › Connection, and an **Exit** button there or in About
    (**proposal**). These are settings anyway, and the settings guidance says settings belong on the
    settings page.
  - When a keyboard-opened menu or panel closes, send `NIM_SETFOCUS` so focus returns to the
    notification area (same Shell_NotifyIcon page).
  - Test with Win+B, the arrow keys, Enter and Shift+F10, with Narrator on.
- **Severity:** high.

### 2.2 The power button is easy to hit by mistake and can't be undone from the PC

- **Where:**
  - Panel header: `src/EdifierMrVolume/VolumeWindow.xaml:58-69`, directly left of the gear at
    `:70-80`.
  - Tray menu: `App.xaml.cs:118-122`, where **Switch speakers off** is the second item, right under
    **Open**.
- **What is wrong:**
  - The power button has the same size, style and weight as the gear. It is about 26 × 26 DIP (a
    14 px glyph plus 6 px padding) and only 8 DIP away from the gear.
  - The gear is used regularly; the power button rarely. One slip turns both speakers off, and they
    come back only with their own power button.
  - In the menu, the item sits under the most-used command, **Open**.
  - This affects everyone, most of all touchpad, touch and motor-impaired users.
- **Guidelines:**
  - [Targeting](https://learn.microsoft.com/windows/apps/develop/input/guidelines-for-targeting):
    "targets that have severe consequences if touched in error should have greater padding and be
    placed further from the edge of the content area".
  - [Dialogs and flyouts](https://learn.microsoft.com/windows/apps/develop/ui/controls/dialogs-and-flyouts/)
    suggest giving an undo instead of confirming every time. Undo isn't possible here.
  - The `winui-design` skill lists "Destructive action fired without confirmation" as an
    anti-pattern.
- **Fix (keeps the owner's "no confirmation"):**
  - Move the button away from the gear: either the left end of the header, or its own labelled button
    at the bottom of the panel ("Turn off speakers" with the power icon).
  - Give it a bigger target, at least 32-40 DIP.
  - In the menu, move it into the last group just above **Exit**, separated from **Open**.
- **Owner decision, reconsider?** No confirmation is the owner's explicit choice (TODO.md,
  2026-11-16).
  - Reason to reconsider: this is the one action in the app that can't be reversed from the PC.
  - A light option that isn't a dialog: a Flyout on the button holding a single **Turn off** button.
    That costs one extra click, and Esc or clicking elsewhere cancels.
  - Another option: a two-step button whose first click turns it into "Turn off?" for 3 s.
- **Severity:** high.

### 2.3 Keyboard focus is lost when the panel switches views

- **Where:**
  - `VolumeWindow.xaml.cs:247-265` (`ShowMainView`, `ShowSettingsView`) and `:427-433` (Esc).
  - `:362-366`: the power button disables itself while it has focus.
  - `:749-760`: the same happens with **Reconnect**.
- **What is wrong:**
  - Pressing the gear collapses the view that holds the focused gear. Back or Esc collapses the view
    that holds the Back button.
  - Nothing moves focus into the new view. Collapsed elements can't hold focus
    ([UIElement.Visibility](https://learn.microsoft.com/uwp/api/windows.ui.xaml.uielement.visibility)),
    so focus ends up wherever the framework puts it, and Narrator says nothing about the new view.
  - Disabling a focused button has the same effect.
  - This affects keyboard and Narrator users.
- **Guidelines:**
  - [WCAG 2.4.3 Focus Order](https://www.w3.org/WAI/WCAG21/Understanding/focus-order.html).
  - [Keyboard accessibility](https://learn.microsoft.com/windows/apps/design/accessibility/keyboard-accessibility).
- **Fix:**
  - After `ShowSettingsView`, focus `BackButton`. After `ShowMainView`, focus `SettingsButton`, which
    returns focus to the control that opened settings.
  - After power-off or Reconnect, put focus on a control that is still enabled.
  - Optionally announce "Settings" with `AutomationPeer.RaiseNotificationEvent`.
- **Severity:** medium.

### 2.4 Moving one band of a preset overwrites the saved Custom curve without warning

- **Where:**
  - Settings › Sound › Equalizer › Bands: `VolumeWindow.xaml.cs:701-712` (`EqSlider_ValueChanged`)
    and `:689-699`.
  - The description: `VolumeWindow.xaml:284`.
- **What is wrong:**
  - While a preset is selected, the band sliders can still be edited.
  - One arrow-key press, or a stray click on a track, turns the choice into Custom and replaces
    `CustomEq` in settings.json. Today that is the owner's own +4/+5/+4/+2 curve.
  - There is no undo. The only warning is half of a long description sentence.
- **Guidelines:**
  - [Dialogs and flyouts](https://learn.microsoft.com/windows/apps/develop/ui/controls/dialogs-and-flyouts/):
    "consider providing a way for the user to undo the action if it was a mistake".
  - [App settings](https://learn.microsoft.com/windows/apps/design/app-settings/guidelines-for-app-settings):
    "Keep your settings simple".
- **Fix:**
  - Simplest: make the sliders read-only while a preset is selected. They still show the preset's
    curve, with the description "Choose Custom to change the bands".
  - Alternative (**proposal**): a one-step "Restore previous Custom curve" link.
- **Owner decision?** TODO.md describes this as the built behaviour ("moving a band makes it the
  Custom curve"). It is not recorded as an owner choice; confirm with the owner.
- **Severity:** medium.

### 2.5 "Custom" means two different things

- **Where:**
  - The main panel's sound-mode bar (`VolumeWindow.xaml:155`).
  - The Equalizer preset list (`src/EdifierMrVolume.Core/EqPresets.cs:18`, `VolumeWindow.xaml.cs:58-59`).
- **What is wrong:**
  - "Custom" is the speakers' sound mode (white light; it plays whatever curve is stored in them). It
    is also one of the four equalizer choices (your own curve).
  - Choosing the preset "Deep bass" switches the speakers to the mode "Custom" (`ApplyEq`,
    `VolumeWindow.xaml.cs:721`). The main panel then says Custom while the equalizer says Deep bass.
  - The main panel never says which curve Custom plays, and has no way to it. The equalizer is in
    Settings by the owner's choice, and that stays.
- **Guidelines:**
  - The ux-copy principle: the same term for the same thing everywhere.
  - [WCAG 3.2.4 Consistent Identification](https://www.w3.org/WAI/WCAG21/Understanding/consistent-identification.html).
  - [Writing style](https://learn.microsoft.com/windows/apps/design/style/writing-style): "Be crisp
    and clear".
- **Fix:**
  - Rename the preset-list item to "Your curve" and keep "Custom" only for the speakers' mode, which
    is Edifier's own name for it.
  - Give the Custom mode item a tooltip such as "Plays the equalizer curve (Deep + balanced). Change it
    in Settings › Sound."
  - Equalizer description: "Heard when the sound mode is Custom."
- **Owner decision?** The owner asked for "2 or 3 presets ... plus custom" and chose the presets; the
  item's label is a wording question for the owner.
- **Severity:** medium.

### 2.6 The notification-area digits ignore contrast themes

- **Where:** `App.xaml.cs:259-266` (`IconColor`) and `TrayIconRenderer.cs`.
- **What is wrong:**
  - The digit colour is white or black, chosen from the registry value `SystemUsesLightTheme`.
  - Contrast themes (Aquatic, Desert, Dusk, Night sky) recolour the taskbar whatever that value is.
  - Example: with Desert's light background and a user whose normal mode was dark, the digits would
    be white on near-white. That is inferred from the code, not tested.
  - The number is the app's main output, and the users affected are exactly those who need contrast.
- **Guidelines:** [Contrast themes](https://learn.microsoft.com/windows/apps/design/accessibility/high-contrast-themes):
  - "Do not hard-code colors".
  - Use SystemColor values.
  - "Test your app in all four built-in contrast themes".
  - Detect them with `Microsoft.UI.System.ThemeSettings.HighContrast`.
- **Fix:**
  - When a contrast theme is on, draw the digits in the system window-text colour
    (`GetSysColor(COLOR_WINDOWTEXT)`), and the "not connected" dash in the grey-text colour.
  - The app already redraws on `UISettings.ColorValuesChanged` (`App.xaml.cs:92`).
  - Test all four themes.
- **Severity:** medium.

### 2.7 Connection problems are small grey text, sometimes technical, and the fix is elsewhere

- **Where:**
  - The message line: `VolumeWindow.xaml:96-103`.
  - The texts: `VolumeWindow.xaml.cs:298-305`.
  - The raw exception text: `src/EdifierMrVolume.Core/SpeakerConnection.cs:346`, with examples at
    `:416-462` and `:619`.
- **What is wrong:**
  - The reason the app can't connect ("Not found yet...", "Bluetooth is off...") is shown in the panel's
    least prominent style: Caption size, secondary colour. Yet it is the one message that matters when
    nothing works.
  - After a failed attempt the line shows the exception message as-is. That can be technical, for
    example "Control characteristic 48090002-... not available (Unreachable)." or Windows' own COM
    error text.
  - The released state says "Right-click the number and tick Connected to speaker". That route is
    mouse-only and leads out of the panel, which has no button for it. Keyboard users can't follow it
    at all (see 2.1).
- **Guidelines:**
  - [InfoBar](https://learn.microsoft.com/windows/apps/develop/ui/controls/infobar): use it "when a
    user should be informed of, acknowledge, or take action on a changed application state";
    connectivity loss is its first example; it has an ActionButton.
  - [Writing style, error messages](https://learn.microsoft.com/windows/apps/design/style/writing-style):
    say what went wrong and what will happen next, offer a realistic solution, avoid jargon.
- **Fix:**
  - Show the reason in an InfoBar (Severity Warning, not closable) with a short title, a message and,
    where it helps, an action button:
    - "Speaker not found" with **Reconnect**.
    - "Bluetooth is off" with **Bluetooth settings**, which opens `ms-settings:bluetooth`
      ([Launch Settings](https://learn.microsoft.com/windows/apps/develop/launch/launch-settings)).
    - "Released for other devices" with **Connect** (**proposal**; it does what the menu tick does).
    - "Turned off" as Informational, with no button.
  - Translate the known errors into plain sentences. Keep the raw text in Diagnostics › Last error.
  - The InfoBar page notes that changed content isn't announced, so close and reopen the bar when the
    message changes.
- **Severity:** medium.

### 2.8 First-run hint: Windows 11 path only, an exe-style name, no shortcut

- **Where:**
  - `VolumeWindow.xaml:84-93`.
  - `src/EdifierMrVolume/EdifierMrVolume.csproj:7-10`: no `AssemblyTitle`, so the installed exe's
    File description is "EdifierMrVolume". I checked this by reading the exe's version resource.
  - `README.md:79-82`.
- **What is wrong:**
  1. The app supports Windows 10 2004+, but "Settings › Personalization › Taskbar › Other system tray
     icons" exists only on Windows 11. Windows 10 calls it "Select which icons appear on the taskbar".
  2. Users have to look for "EdifierMrVolume" rather than the product name, and Task Manager shows the
     same string.
  3. The title is a 10-word sentence.
  4. Nothing opens the right Settings page for them.
- **Guidelines:**
  - [Notification area](https://learn.microsoft.com/windows/win32/uxguide/winenv-notification):
    users promote icons themselves; "there is no way for your program to perform this promotion
    automatically".
  - [InfoBar](https://learn.microsoft.com/windows/apps/develop/ui/controls/infobar) ActionButton.
  - [Launch Settings](https://learn.microsoft.com/windows/apps/develop/launch/launch-settings) lists
    `ms-settings:taskbar`.
  - [Writing style](https://learn.microsoft.com/windows/apps/design/style/writing-style): "Lead with
    what's important".
- **Fix:**
  - Set `<AssemblyTitle>Edifier MR Volume</AssemblyTitle>`, then check what the Settings list shows
    and update the hint and README to match.
  - Choose the path text by OS build (22000 and later is Windows 11).
  - Title: "Your volume is in the notification area". Message: "If you don't see the number, Windows
    hid it. Turn it on in taskbar settings."
  - Add an action button, **Taskbar settings**, that opens `ms-settings:taskbar`.
- **Severity:** medium.

### 2.9 Sound mode uses a navigation control and has no visible label

- **Where:** `VolumeWindow.xaml:146-156` and `VolumeWindow.xaml.cs:334-360`.
- **What is wrong:**
  - SelectorBar is meant for switching between views or pages. Here it changes a hardware setting
    immediately, and its tab-underline look suggests it changes what the panel shows.
  - The label "Sound mode" exists only for Narrator. Sighted users see three bare words under the
    slider.
  - SelectorBar selects its first item when it gets focus with nothing selected, and raises
    `SelectionChanged` on Tab focus and on the arrow keys. If the speakers ever report a mode outside
    0-2 (`UpdateSoundMode` returns early at `:341`), tabbing into the bar would select Monitor and send
    it to the speakers.
- **Guidelines:**
  - [SelectorBar](https://learn.microsoft.com/windows/apps/develop/ui/controls/selector-bar): "Use a
    SelectorBar when you want to let a user navigate between a limited number of views or pages". The
    same page describes the focus auto-selection.
  - The Toolkit's [Segmented](https://learn.microsoft.com/dotnet/communitytoolkit/windows/segmented/)
    control is described as "A common UI control to configure a view or setting", for 2-5 items.
  - [WCAG 3.3.2 Labels or Instructions](https://www.w3.org/WAI/WCAG21/Understanding/labels-or-instructions.html).
- **Fix:**
  - Use the Toolkit's `Segmented` control (the same toolkit family the app already uses) or
    `RadioButtons` with `MaxColumns="3"`.
  - Add a small visible label, "Sound".
  - Add a tooltip per mode from the manual: Monitor = neutral, red light; Music = warmer, green light;
    Custom = equalizer, white light.
  - For an unknown mode, clear the selection and disable the control.
- **Owner decision?** No. The owner picked the feature; the control was an implementation choice.
- **Severity:** medium.

### 2.10 The header row can push the buttons out at large text sizes (likely; not verified)

- **Where:**
  - The header: `VolumeWindow.xaml:38-81`.
  - The "Close after" radios: `VolumeWindow.xaml:198-211`.
- **What is wrong:**
  - The header's columns are the model name (`*`), the status text (`Auto`), power (`Auto`) and gear
    (`Auto`), inside a panel fixed at 340 DIP wide. Only the height follows the content.
  - At 225 % text, "Looking for speaker..." needs roughly 260 DIP. Auto columns are measured first, so
    the model name gets no width and the row runs past the right edge, clipping the gear, which is the
    only way into settings.
  - The four "Close after" radios sit in a horizontal StackPanel that doesn't wrap, so "5 s" can be
    clipped the same way.
- **Guidelines:**
  - [Text scaling](https://learn.microsoft.com/windows/apps/develop/input/text-scaling): "controls
    and containers must also resize and reflow"; "Use text wrapping".
  - [WCAG 1.4.4 Resize Text](https://www.w3.org/WAI/WCAG21/Understanding/resize-text.html).
- **Fix:**
  - Put the status text on its own line under the model name.
  - Or widen the panel with the text scale (`UISettings.TextScaleFactor`, capped).
  - Check at 200 % and 225 %.
- **Severity:** medium if confirmed.

### 2.11 "Close after" uses loose RadioButtons

- **Where:** `VolumeWindow.xaml:198-211`.
- **What is wrong:**
  - Four separate RadioButtons in a StackPanel: each one is a Tab stop, and the group has no name.
  - The SettingsCard copies its header into the content's Name, but a StackPanel isn't exposed to UI
    Automation, so Narrator never says "Close after"
    ([SettingsCard source](https://github.com/CommunityToolkit/Windows/blob/main/components/SettingsControls/src/SettingsCard/SettingsCard.cs)).
  - "1 s" may be read as a letter.
- **Guidelines:**
  - [Radio buttons](https://learn.microsoft.com/windows/apps/develop/ui/controls/radio-button): "We
    recommend the RadioButtons control. This control simplifies layout, handles keyboard navigation and
    accessibility".
  - [WCAG 1.3.1](https://www.w3.org/WAI/WCAG21/Understanding/info-and-relationships.html).
- **Fix:**
  - Use `<RadioButtons MaxColumns="4" AutomationProperties.Name="Close after">`.
  - Set the item names to "1 second" and so on. Radio buttons stay, as earlier agreed with the
    guidelines.
- **Severity:** low.

### 2.12 Mute is a plain button whose name flips

- **Where:** `VolumeWindow.xaml:111-121` and `VolumeWindow.xaml.cs:409-416`.
- **What is wrong:**
  - It is a Button whose name switches between Mute and Unmute.
  - UI Automation sees no on/off state, and Narrator doesn't announce the change when it is pressed.
- **Guideline:** [WCAG 4.1.2 Name, Role, Value](https://www.w3.org/WAI/WCAG21/Understanding/name-role-value.html).
- **Fix:**
  - Make it a `ToggleButton` (checked = muted) with the fixed name "Mute" and the same icon swap.
  - Narrator then says "Mute, toggle button, on/off".
- **Severity:** low.

### 2.13 Volume limit and Start volume hide their numbers

- **Where:** `VolumeWindow.xaml:216-261` and `VolumeWindow.xaml.cs:489-499`.
- **What is wrong:**
  - The level sits in the collapsed expander. The descriptions say "never sets the volume higher" and
    "Sets this level" without saying which level.
  - Turning on Volume limit quietly applies 24 (`Support.cs:33`), and users must expand the card to
    learn that.
- **Guidelines:**
  - [App settings](https://learn.microsoft.com/windows/apps/design/app-settings/guidelines-for-app-settings):
    "the app should immediately reflect the change".
  - The ux-copy principle "Useful".
- **Fix:** while a switch is on, put the number in its description:
  - "The app won't set the volume above 24. The knob on the speaker can still go higher."
  - "Sets the volume to 10 whenever the app connects, for example at sign-in."
- **Severity:** low.

### 2.14 Small icon buttons

- **Where:** the gear and power buttons (`VolumeWindow.xaml:58-80`), Mute (`:111-121`) and Back
  (`:166-175`).
- **What is wrong:**
  - 6 px of padding around a 14 px glyph gives a hit area of about 26 × 26 DIP; Mute is about 30 × 30.
  - That passes the WCAG 2.2 minimum of 24 px but is well below Microsoft's 40 px recommendation. It
    matters most for the power button (2.2).
- **Guidelines:**
  - [Targeting](https://learn.microsoft.com/windows/apps/develop/input/guidelines-for-targeting): 40 × 40.
  - [WCAG 2.5.8](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html).
- **Fix:** padding of 9-10 (about 32 × 32) for gear, Back and Mute; the power button as in 2.2.
- **Severity:** low.

### 2.15 Notification-area menu: a label, the order and the default item

- **Where:** `App.xaml.cs:115-127`; the check state is set at `:289`.
- **What is wrong:**
  1. "Connected to speaker" names a state, and it is ticked whenever the app is *trying* to connect
     (`IsRunning`). While the app is searching, it says "connected" when it isn't.
  2. The power item sits under **Open** (see 2.2).
  3. The default command, **Open**, isn't bold.
  4. Some commands are only in this menu (see 2.1).
- **Guideline:** [Notification area, Context menus](https://learn.microsoft.com/windows/win32/uxguide/winenv-notification):
  - "Open (default, list first, in bold)".
  - Check marks for state.
  - Separator, then "Exit" last.
- **Fix:**
  - Rename the item to "Connect to speaker", ticked when the app may hold the link.
  - New order: Open · separator · Connect to speaker · Start with Windows · separator · Turn off
    speakers · Exit.
  - Make Open bold if H.NotifyIcon's PopupMenu mode supports a default item (not checked).
- **Severity:** low.

### 2.16 The equalizer band sliders don't show where flat is

- **Where:** `VolumeWindow.xaml.cs:639-682` (`BuildBandRows`); see `eq_panel.png`.
- **What is wrong:**
  - Each slider fills with the accent colour from the left end (-6) to the thumb, so a -2 cut looks
    like a long bar, just as a boost does.
  - Flat (0) is only "the middle", and there are no range labels.
  - The track is short: about 110 DIP for 13 steps.
- **Guideline:** [Sliders](https://learn.microsoft.com/windows/apps/develop/ui/controls/slider):
  - "Use labels to show the range of values".
  - "Use tick marks when you want to show users the location of major or significant values".
  - "Size the control so that users can easily set the value they want".
- **Fix:**
  - Add a scale row above the bands ("-6 · 0 · +6", or "Cut" and "Boost").
  - Use lightweight styling so the fill doesn't suggest an amount.
  - Shrink the indent so the tracks get longer.
- **Severity:** low.

### 2.17 Wording: one term per thing, Windows' verbs, US spelling

- **Where:** XAML, code-behind and `SpeakerConnection.cs` texts.
- **What is wrong, and the suggested text:**
  - **Switch / turn.** "Switch speakers off", "Switch off when the PC sleeps", "Switch on ... to choose"
    (three cards) and "Switched off. Switch the speakers on...": Microsoft says "turn on, turn off" for
    devices and toggles. Owner wording: the owner's own notes say "switch", so this is the owner's
    call.
  - **Power saving description.** "Switched off, they use more power" is ambiguous: the setting, or
    the speakers? Suggested: "After 15 minutes without sound, the speakers go to standby. When this is
    off, they stay on and use more power. The knob changes this setting too."
  - **"tick".** British English. Use "select".
  - **Speaker / speakers.** The same thing is "the speaker" in "Connected to speaker", "Looking for
    speaker..." and "the knob on the speaker", but "the speakers" in "Switch speakers off" and "the
    speakers go to standby". Suggested rule: "speakers" for the pair (power, sound); "speaker" only for
    the one with the knob and Bluetooth.
  - **One state, four labels.** "Disconnected" (panel), "disconnected" (tooltip), "Released (not
    connected)" (Diagnostics) and "Released, so another device..." (message). Also "Looking for
    speaker..." against "Looking for the speaker". Pick one form of each.
  - **Small fixes.**
    - "Last error: none" → "None".
    - "MIT licence" → "MIT License", US spelling like the rest.
    - Diagnostics "Bluetooth" → "Bluetooth address".
  - **"Deep + balanced".** A symbol in a name. The names came with the presets the owner chose, so
    they are the owner's call.
  - **Bands description.** Split it into "Steps up or down from flat (0)." plus one sentence on what
    changing a band does, which depends on 2.4.
- **Guidelines:**
  - [Microsoft Style Guide: turn on, turn off](https://learn.microsoft.com/style-guide/a-z-word-list-term-collections/t/turn-on-turn-off).
  - [Writing style](https://learn.microsoft.com/windows/apps/design/style/writing-style).
  - ux-copy "Consistent".
- **Severity:** low.

### 2.18 Installer and README wording are out of date

- **Where:**
  - `packaging/inno/edifier-mr-volume.iss:33` still says "in the Windows tray".
  - `README.md:3`, `9`, `31-43` and `79-82`: "tray", **Show volume**, and no mention of mute, volume
    limit, start volume, sound mode, equalizer, power-off, power saving or the wheel.
  - The screenshots `docs/images/flyout.png` and `settings.png` are outdated.
- **What is wrong:**
  - TODO.md says the "notification area" wording is done "in the app, installer and shortcut", but the
    installer's file description still says "tray".
  - The README is an open TODO item.
- **Guideline:** [Notification area, Documentation](https://learn.microsoft.com/windows/win32/uxguide/winenv-notification):
  "Refer to the notification area as the notification area, not the system tray".
- **Fix:** finish the open TODO item and retake the screenshots once the display is on.
- **Owner decision:** in line with the owner's choice of "notification area".
- **Severity:** low.

### 2.19 The volume can jump to 30 with one key or click

- **Where:** `VolumeWindow.xaml:122-133`.
- **What is wrong:**
  - Standard slider behaviour: End jumps to 30, and a click near the right end of the track jumps
    there too. Studio monitors at 30 are very loud.
  - Volume limit, which would cap this, is off by default.
- **Fix (owner to pick):**
  - Leave it as is.
  - Or handle Home/End as single steps.
  - Or turn Volume limit on by default (for example at 24). The 2026-11-15 review called "nothing
    optional on by default" fine as is; that was the reviewer's view, not an owner decision.
- **Severity:** low.

### 2.20 A wheel notch down above the limit drops straight to the limit

- **Where:** `App.xaml.cs:156-172`; the `Clamp` is at `:166`.
- **What is wrong:**
  - Suppose the knob has the volume at 28 and the limit is 24. One notch down goes to 24, not 27.
  - That is the safe direction, but it breaks the owner's "one step per notch, never more".
  - Nothing in the app mentions the wheel. The infotip shouldn't explain interactions, per the
    notification-area guidance.
- **Fix:**
  - A notch down should always go one step down, and a notch up should never go above the limit.
  - Mention the wheel in the first-run hint and the README.
- **Owner decision?** The owner's two rules (one step per notch; the limit applies) conflict here;
  the owner picks.
- **Severity:** low.

### 2.21 The app icon almost disappears on dark backgrounds

- **Where:** `assets/icon/app-icon-small.svg` (cabinet `#232427`, edge `#5C5D62`) and
  `app-icon.svg`. Seen in Start, Installed apps and the About row.
- **What is wrong:**
  - Computed contrast on Windows' dark surface (#202020): cabinet about 1.05:1, edge about 2.5:1.
    Only the copper parts pass 3:1 (about 3.3:1).
  - On light backgrounds it is fine (about 14:1).
  - In `icon_preview.png`, the small dark-row sizes read as copper dots.
- **Guideline:** [App icon design](https://learn.microsoft.com/windows/apps/design/iconography/app-icon-design):
  "Make sure at least half of your icon passes a 3.0:1 contrast ratio on light and dark theme". It
  also allows separate light and dark assets.
- **Fix:**
  - Lighten the edge to about `#8A8B90` or lighter (about 4.8:1 on #202020 and 3.1:1 on #F3F3F3) and
    thicken it slightly in the small icon.
  - Or add a dark-theme variant with a graphite cabinet.
- **Owner decision, reconsider?** The owner asked for the satin black MR4.5 look. A lighter rim keeps
  that look.
- **Severity:** low.

### 2.22 The settings view resets whenever focus leaves

- **Where:** `VolumeWindow.xaml.cs:147-154` and `:267-278`.
- **What is wrong:**
  - Light dismiss is correct for this panel.
  - But **Open log**, the GitHub links, a toast notification or a click on the taskbar hide it, and
    the next open shows the volume view with every expander collapsed.
  - So tweaking the equalizer while switching to a music player means the gear plus an expand each
    time.
- **Guideline:** [Notification area](https://learn.microsoft.com/windows/win32/uxguide/winenv-notification):
  "Dismiss the popup window if the user clicks anywhere outside the window". Light dismiss itself is
  right.
- **Fix (proposal):** if the panel was hidden less than about 30-60 s ago, reopen it to the settings
  view with the same expanders open.
- **Severity:** low.

### 2.23 Code-review notes (winui-code-review)

- **AutomationIds.** Named elements fall back to `x:Name`, but some interactive controls have no
  AutomationId at all:
  - **Open log** (`VolumeWindow.xaml:362`).
  - The EQ sliders created in code (`VolumeWindow.xaml.cs:651`).
  - The three SelectorBar items (`VolumeWindow.xaml:153-155`).

  The owner's UI Automation tests would benefit from stable IDs.
- **Settings view loading.** About 40 toolkit controls are created with the window and live for the
  app's lifetime, though the view is opened rarely. `x:Load="False"` (loading it on the first gear
  click) could save memory in an always-running app. Measure first, as CLAUDE.md asks.
- **Time format.** `HH:mm:ss` is hard-coded (`VolumeWindow.xaml.cs:730`, `SpeakerConnection.cs:347`).
  Use the user's time format, for example `ToString("T", CultureInfo.CurrentCulture)`.
- **Hard-coded strings.** Strings are hard-coded rather than in `.resw` files. That is fine while the
  app is English-only.
- **No MVVM.** There is no MVVM; the logic is in code-behind. That is fine at this size and not worth a
  refactor.
- **Severity:** low.

## 3. Fine as is

- **Notification area:**
  - The icon shows status as a changing quantity, which the guidance explicitly allows.
  - The tooltip "Edifier MR4.5 volume: 19 of 30" follows the infotip format "device name status
    statement".
  - A left click opens a popup next to the icon, with no double-click delay.
  - The popup dismisses on a click outside or on Esc.
  - It is placed next to the taskbar wherever the taskbar is.
- **Auto-open panel:** it doesn't take focus, it stays away during full-screen apps, and it is off by
  default. Nothing optional is on by default.
- **Digits:** drawn at the real icon size in a condensed font. The dimmed dash for "not connected" is
  not colour-only, and its contrast is about 6.6:1 on a dark taskbar and 5.3:1 on a light one (computed
  for typical taskbar colours).
- **Settings structure** matches Microsoft's app-settings guidelines:
  - SettingsCard and SettingsExpander, with one-word BodyStrong section headers.
  - Changes take effect immediately.
  - Disabled cards say why ("Switch on ... to choose", "Available once the app is connected...").
  - About is an expander at the bottom with icon, name and version.
  - The external links are clickable cards with an open-in-new icon.
  - It is a single scrolling column.
- **Toggle switches** use the default On/Off with noun labels.
- **Equalizer:**
  - The band sliders are horizontal in a vertically scrolling page.
  - Tick marks are shown.
  - The values are honest steps, not invented dB.
  - The minus sign is a real minus.
  - Writes go out only after acks.
- **XAML theming and type:** every colour is a ThemeResource, text uses the built-in styles, the
  transient surface uses acrylic, and there are no custom control templates.
- **Accessible names** are set on every icon-only button, the volume slider, all switches, the number
  boxes, the preset box and the mode bar. The Mute name follows the state (but see 2.12).
- **Text scaling:** the panel measures its content and resizes, capped to the work area with
  scrolling. Width is the exception (2.10).
- **Clear texts:**
  - Volume limit and Start volume say exactly what they do.
  - The "Switched off..." message tells the user how to get the speakers back.
  - The "Not found" hint appears after 15 s.
  - The Diagnostics values can be selected, with Reconnect and Open log next to them.
- **Wheel over the number:** the hook exists only while the pointer is over the icon, so it stays
  light, and a notch up never lowers the volume.
- **Copy in general:** sentence case, "you", short sentences, no blame.
- **Number of settings:** six items, a little over the guideline's "four or five", but grouped well.

## 4. What I could not check

- **No current screenshot of the main panel.** The display is off. The main panel was reviewed from
  `VolumeWindow.xaml`; `docs/images/flyout.png` and `scratchpad/firstrun.png` show an older layout
  without the power button and the mode bar. Still unseen:
  - the header's crowding (2.10);
  - how the power button and gear look side by side;
  - how the SelectorBar sits under the slider.
- **Settings screenshots.** `powersave_panel.png` and `settings_shot.png` are current. `eq_panel.png`
  has an older Equalizer description.
- **Nothing was run.** Unverified:
  - keyboard behaviour of the notification-area icon (2.1 is from reading H.NotifyIcon's source);
  - where focus lands after a view switch;
  - what Narrator announces;
  - contrast themes (digits, acrylic fallback, toggle and slider colours);
  - light theme, 200 % and 225 % text, Windows 10, multi-monitor and side or top taskbars;
  - whether the panel gets keyboard focus when opened by starting the app again from Start. Windows
    may refuse to bring a background process's window to the foreground; the second instance could
    call `AllowSetForegroundWindow` before signalling, but that is untested.
- **Settings label.** Which string Windows 11 shows in "Other system tray icons". Very likely the
  exe's File description ("EdifierMrVolume"); not confirmed.
- **Contrast figures.** Computed from colour values in the code and SVGs, not measured on screen.
- **Tools not run:**
  - WinApp CLI is not installed, so `winapp find-ui` and `winapp find-api` weren't run.
  - The WinUI analyzer (`winui-dev-workflow`) wasn't run, so its WUI rule checks didn't run.
  - `winui-ui-testing` wasn't used, because it needs the running app.
- **Owner checks.** No user testing; the owner should try 2.1, 2.3 and 2.10 by hand with Narrator.
