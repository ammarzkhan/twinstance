# Changelog

Notable changes to Twinstall. Format follows [Keep a Changelog](https://keepachangelog.com/).

## [0.5.2] — 2026-08-09

### Fixed — the result screen could name another app's profile as yours

Setting up OpenCode, step 2 stated *"Your existing account lives in kimi-desktop"* with a green
tick. That folder belongs to Kimi. Going along with it would have pointed the default account at
a different application's data.

The ranking was never wrong — it returns *ambiguous* for exactly this case and always has. The
screen printed the top-ranked guess regardless, so a fallback was rendered as a confirmed fact.
It now says it could not tell, when it could not tell.

### Changed — an app can recognise its own profile folder again

Matching a profile folder required the name to be exactly the app's own, and almost no app does
that: Kimi keeps its data in `kimi-desktop`, OpenCode in `ai.opencode.desktop`. Neither could
identify its own folder, which is what left the screen guessing above.

A folder now also matches on its separator-delimited parts, with two deliberate limits:

- Parts split on `-`, `_`, `.` and space, and **not** on camel case — so `DiscordCanary` stays one
  part and is never read as Discord. It is a different application with its own profile.
- A folder carrying a variant word — canary, ptb, insiders, beta, nightly and the rest — never
  matches, which is what stops `Code - Insiders` being taken for Visual Studio Code.

Both limits fail the same way on purpose. A missed match costs one click; a wrong match silently
points an account at another app's data.

### Added — Kimi, and an app the list no longer offers

**Kimi** is supported and measured: version 3.1.7 honours `--user-data-dir` and creates a separate
profile, so two accounts work. Note it claims both `kimi://` and `kimi-work://`; Twinstall takes
only the first.

**OpenCode is deliberately absent from the list**, having been measured as impossible to support.
It takes `--user-data-dir`, discards it, and spawns every child process with its own fixed path —
and redirecting `%APPDATA%` does not move it either, because Electron resolves that folder
through Windows rather than the environment. It stays in the preset file with the evidence
attached, so the question does not have to be investigated twice, and *Choose another app…* still
accepts it and still explains itself.

## [0.5.1] — 2026-08-08

### Fixed — the release page told you to download a file that does not exist

The notes template carries a `<version>` placeholder and nothing was filling it in, so 0.5.0's
release page named `Twinstall-<version>-standalone.exe` where the file is
`Twinstall-0.5.0-standalone.exe`. The assets listed above it were right; the sentence pointing at
them was not. The tag now substitutes it, and the rendered body is echoed into the build log so
the next mistake of this shape is visible before anyone downloads anything.

### Fixed — narrowing the window pushed every row out of its panel

Dragging the window down towards its 660×600 minimum left every row wider than the panel holding
it. AutoScroll answered with a horizontal scrollbar, and the rows ran underneath the vertical one.
Widening again left a ragged right margin instead.

A step's controls are positioned absolutely and sized from the panel's width once, while the page
is being built, and `Relayout` cannot simply rebuild them: a rebuild disposes controls that may
still be inside their own `Click` handler, which is the whole reason `ShowLater` exists. So the
children that should track the panel are anchored as they are added, and WinForms does the work.

Verified against the running window at 660×600 rather than reasoned about: rows fill the panel,
no horizontal scrollbar appears, and long paths wrap instead of overflowing.

### Fixed — the review screen hid the one warning it exists to give

The taskbar option on step 4 changes a Windows setting that applies to **every application on
the system**, not just the one being duplicated. The sentence saying so sat under the checkbox,
at the bottom of the page, below a list whose length depends on the machine — and on a normal
setup with two accounts that list is eight lines long, which pushed the sentence clean off the
bottom of the panel. The checkbox was visible and the explanation was not, which is the wrong
half to lose. Nothing was ever applied without asking, but the asking left out the part that
matters.

- **Both options now sit above the list of changes**, not below it. The list is the part that
  varies — scheme, badges, and the two options each add a line — so it is the part that scrolls
  when something has to. The two explanations are at a fixed place near the top and are on
  screen at every window size down to the minimum.
- **Line heights are measured rather than reserved.** Every bullet was given 34px — room for two
  lines — because two of them are file paths that wrap when the window is narrow or the user's
  name is long. The other six hold one 17px line and were given the same 34, wasting more space
  than the page was over by. Heights now come from the text, so the long lines still wrap and the
  short ones cost what they use. The page still scrolls at the default window size when both
  options are ticked — that was measured, not assumed — but everything that explains a choice is
  above the fold, and only the list of changes falls below it.
- **Ticking an option no longer moves it.** Each tick adds or removes a bullet; with the bullets
  above, both checkboxes jumped a row on every click — the one just clicked slid out from under
  the pointer, and the other took its place.
- The taskbar checkbox rebuilt the page from inside its own event handler, disposing the control
  still running it. It defers by one message now, like every other control on the page.

## [0.5.0] — 2026-08-08

### Fixed — installing from a build tree produced a copy that could not launch an account

**Released builds were never affected**, which is why this survived unnoticed: `publish.ps1` sets
`PublishSingleFile`, and the bundle carries everything. It only ever bit an install made from a
framework-dependent build output — which is what anyone working on Twinstall has.

`CopyProgram` copied the flat files plus `presets`, and skipped every other folder. But the
`System.Management.dll` sitting beside the executable in a build tree is the 73 KB *reference*
assembly; the real Windows implementation is the 312 KB one under `runtimes\win\lib\net8.0`, and
`deps.json` points the .NET host at it through `runtimeTargets`. The installed copy therefore
started, drew its window and badged icons, then died with `FileNotFoundException` the moment
anything enumerated processes — which is the launch path, so every account shortcut was broken
while the application itself looked healthy.

`runtimes` is now copied too, recursively, because `runtimes\win\lib\net8.0` is four levels down
and the previous single-level copy would have produced an empty folder.

### Fixed — a Store update no longer breaks the setup

Updating a Microsoft Store app broke Twinstall until it was set up again from scratch. Every
launch reported *"Twinstall cannot find the application it was set up for"*, and nothing the
user had done caused it.

Windows carries the version inside an MSIX install path, so `Claude_1.25927.0.0_x64__pzs8sxrjxfjjc`
becomes `Claude_1.26832.0.0_x64__pzs8sxrjxfjjc` the moment the Store updates the app, and the old
folder is deleted. The saved configuration recorded that absolute path, which made it a value with
an expiry date: correct when written, invalid at the next update, for every packaged app and every
user. It was caught when Claude updated underneath a working install on 8 August 2026.

Twinstall now notices the app moved and follows it. The package *family* — `Claude_pzs8sxrjxfjjc`
— is stable across versions, so the stale path is enough to identify the new one, and the layout
below the package root carries over unchanged. The configuration is rewritten and the launch
proceeds. It is a log line, not a dialog: the user had no part in this and has nothing to decide.

Unpackaged applications are deliberately left alone. A missing `slack.exe` under `%LOCALAPPDATA%`
means it was uninstalled or moved, there is no second candidate to try, and substituting some other
executable would be far worse than reporting it as missing.

### Fixed — the only way to undo a setup was hidden on PCs with several apps installed

"Remove Twinstall's changes" sat at the bottom of step 1, under the list of applications found
on the PC — and the length of that list is decided by the machine, not by the program. Twinstall
knows about twenty apps; a PC with five of them installed produced a page 502px tall inside a
448px panel, so the button was below the fold, reachable only by scrolling. Nothing else in the
window removes a setup once it is finished, and step 1 is the one screen nobody thinks to scroll:
everything it asks for is at the top.

**The button has moved into the footer**, on the left of the row that already holds Back and
Continue. The footer is measured from the bottom edge of the window, so the button is in the same
place whether the PC has one known app or twenty, and it cannot be pushed anywhere by the list
above it. It still appears only when there is a saved setup to remove. The status line beside it
gives up the width the button takes, and gets it all back on the screens where the button is
absent — every other step's footer is unchanged, to the pixel.

Two things follow from it no longer being page content:

- **Step 1 now fits with no scrollbar at all** at the default window size on a five-app machine,
  where before the scrollbar appeared purely because of this button.
- **The button no longer destroys itself mid-click.** It used to be rebuilt with the page, and
  removing a setup rebuilds the page — from inside the button's own `Click` handler, disposing
  the control still executing it. That is the exact hazard `ShowLater` was added for. As
  permanent chrome it is never disposed, so the deferral is not needed and the risk is gone.

## [0.3.0] — 2026-08-07

*Withdrawn; superseded by 0.5.0.*


### Added — it is one file now, and it installs itself

Sharing it previously meant handing someone a zip, which they unpacked, then hunted for the
executable among a handful of DLLs, then ran from wherever it landed. That last part is not
cosmetic: shortcuts and the registered protocol handler both record an absolute path, so a copy
run from Downloads stops routing sign-ins the moment that folder is tidied up, with nothing to
say why.

- **Releases are single executables.** `Twinstall-<version>.exe` (~0.6 MB, needs the .NET 8
  Desktop Runtime) and `Twinstall-<version>-standalone.exe` (~147 MB, needs nothing). The
  presets are embedded, so a single file really is a single file.
- **First run offers to install.** It copies itself to `%LOCALAPPDATA%\Programs\Twinstall`, adds
  a Start-menu entry for itself, registers in Settings → Apps → Installed apps, and relaunches
  from there. Declining runs it in place, which is fine for a look.
- Neither build enables single-file compression. That gets the output quarantined mid-bundle —
  see [SECURITY.md](SECURITY.md).

### Fixed

- **Renaming an account left its old shortcut behind for ever.** Shortcuts were only ever added,
  never reconciled, so a Start menu accumulated one entry per name an account had ever had.
  Twinstall's own shortcuts are now cleared before writing the current set.

  They are identified by *where they point* — a target named `Twinstall.exe` with a `--launch`
  argument — not by name, so a desktop is never swept for a pattern. Matching on the full path
  was tried first and was wrong: shortcuts left by an earlier install location point at the old
  folder, which is precisely the stale case worth removing.

### Changed — the management UI

Rebuilt as a four-step flow instead of one page. The old window showed app selection, a raw
detection dump, an empty grid and the apply controls all at once, so the first thing a new user
saw was four things they could not do yet.

- **Only apps that are actually installed are offered.** A dropdown listing twenty apps, fifteen
  of which you do not have, is not a menu — it is a quiz. The list is now built by resolving
  every preset against this machine.
- **Results are in plain language**, as ticked statements — "Built on Chromium, so it supports
  separate profiles", "Sign-in links use slack:// — Twinstall can route those" — with the
  technical dump moved behind a *Technical details* toggle for when it is wanted.
- **Follows the system theme**, light or dark, including the title bar, and uses the user's own
  Windows accent colour rather than a hard-coded brand colour.
- Accounts are shown as rows with their badge colour, not a grey grid; the colour picker is
  swatches rather than a system colour dialog.
- Headings use the app's real name. `Path.GetFileNameWithoutExtension` produced "slack can do
  this"; it now prefers the preset's display name, then the binary's `ProductName`.

### Fixed — the last step could be skipped without noticing

The final screen said "You're set up" and offered **Finish** whether or not the scheme handler
had actually been chosen. Everything else can succeed — profiles, shortcuts, badged icons — and
the product still not do its job, because a sign-in will keep landing on the wrong account. The
one step that matters was the one the app was quietest about.

- The screen now checks whether Windows is really handing us the scheme, and says **"One step
  left"** with a red mark when it is not.
- It re-checks by itself whenever the window is activated, so coming back from Settings answers
  "did that work?" without being asked.
- **Finish** becomes **Finish anyway** and explains what will still be broken before closing.
- The check reads the **UserChoice** key, not just `HKCU\Software\Classes\<scheme>`. Choosing an
  app in Settings writes the former and leaves the latter alone, so the obvious check reports
  failure at the exact moment the user has just succeeded.

### Added

- **One button that lands on the right page.**
  `ms-settings:defaultapps?registeredAppUser=Twinstall` opens Settings at
  *Apps → Default apps → Twinstall*, where the scheme is the only thing listed and
  "Choose a default" is one click away. No searching, and no instructions for navigating there
  by hand — a button that opens the page makes them noise.

  Three approaches were tried; the two failures are recorded so nobody repeats them:

  - **Opening a link of the scheme does nothing** while nothing is registered for it.
    `ShellExecute` returns without starting anything *and without raising an error*. Windows
    has a "how do you want to open this?" chooser for unknown file types, not for URL protocols.
  - **`IApplicationAssociationRegistrationUI::LaunchAdvancedAssociationUI`**, the API documented
    for precisely this, now shows a message box reading *"To change your default apps, go to
    Settings > Apps > Default apps"* and opens nothing. Deprecated in all but name.
  - Plain `ms-settings:defaultapps` lands on a page with two search boxes, where typing the
    scheme into the wrong one reports "We couldn't find anything to show here".

  Windows blocks programmatic changes to this setting deliberately — it is how browsers used to
  hijack one another — so no app can set it for the user. Being one click away is the ceiling,
  and that is what this now achieves.

- **A drawn walkthrough of the two remaining clicks**, as a schematic rather than a screenshot:
  a picture of the Settings app would be Microsoft's artwork inside our binary, which is the
  same thing we avoid for every other vendor.

- **A rollback offer when setup is abandoned.** Closing the window with the handler unset means
  leaving a machine that has been changed but does not work. It now offers to undo the
  shortcuts, icons, registry entries and — only if this run enabled it — the taskbar setting.
  Profile folders are never touched: they hold live sessions, and deleting an account someone
  has signed into because they closed a window would be indefensible.

- **A logo, and a colour of its own.** The mark is the same rounded tile twice, told apart by
  colour — which is the product in one shape. `assets/logo.svg` is the source;
  `scripts/make-logo.ps1` draws the identical geometry with GDI+ and emits the PNG set, a
  multi-size `.ico`, and the MSIX tile assets, so nothing is hand-drawn or unreproducible.

  The accent is **teal**, chosen against two constraints rather than taste. Twinstall's icon
  sits in the taskbar directly beside Slack, Discord, VS Code and Claude, and must not read as
  an official add-on for any of them — so aubergine, blurple and Microsoft blue were out. And
  the per-account badge colours are the signal this product exists to provide, so the app's own
  chrome has to stay clear of that palette instead of competing with it. Cyan was dropped from
  the badge palette for the same reason.

  This replaces following the Windows accent colour. That is the right behaviour for a system
  utility and the wrong one for something with a brand of its own.

  `assets/twinstall.ico` is the one exception to the repository's no-icons rule. That rule
  exists to keep *other vendors'* artwork out, and is unchanged for every other case.

- `Twinstall.exe --preview <1-5>` — opens one screen directly with representative data, so a
  layout change can be looked at without clicking through the flow. Seeds nothing, starts
  nothing, applies nothing.

### Fixed

- **Preset lookup failed for every Microsoft Store app.** A normal application cannot *list*
  `C:\Program Files\WindowsApps` — the ACL grants traverse but not read, so `Directory.Exists`
  returns true while `Directory.GetDirectories` throws `UnauthorizedAccessException`. The
  exception was being swallowed, so choosing "Claude" simply filled in nothing. Slack, under
  `%LOCALAPPDATA%`, worked fine, which is what made the failure look arbitrary.

  Install paths are now read from `MrtCache` under HKCU, which is readable without elevation,
  and each candidate is confirmed against disk because that key remembers uninstalled versions.

  Worth recording: an interactive shell *can* list `WindowsApps`, so testing from a terminal
  never reproduced this. Neither elevation nor process bitness was the cause — both were
  checked and ruled out. Only running from the application's own process showed it.

- **The UI failed silently.** A preset that resolved to nothing, "Check this app" with no
  application chosen, and "Add" before checking one all reported into a status label at the
  bottom of the window, so they read as "nothing happens when I click". Anything the user has
  to act on now says so in a dialog.

### Added

- `Twinstall.exe --presets` — traces every preset hint, what it expands to, and the actual
  exception when it finds nothing. Answers "why didn't it find my app?" without guesswork.
- Preset list grown from 5 apps to 20: Loom and ClickUp (both measured), plus Cursor, Obsidian,
  Notion, Figma, GitHub Desktop, 1Password, Bitwarden, Joplin, Element, Postman, Insomnia,
  Evernote and Superhuman (inferred layouts, never run here).
- Every preset now carries a `provenance` field — `measured` or `inferred` — so the difference
  between "we ran the probe against this" and "this follows a usual installer convention" is
  visible rather than implied.

- New `ProbeVerdict.LaunchBlocked`, for when Windows refuses to start the target at all. That
  is a different answer from `NotHonoured` — we did not learn that the app ignores
  `--user-data-dir`, we learned we could not ask — and collapsing the two would claim something
  unearned. Found on OpenAI Codex, a Store app whose executable denies `CreateProcess` by every
  route while Claude's, in the same folder with byte-identical ACLs, starts normally.

### Changed

- `apps.json` states explicitly that it is **not** a compatibility matrix. Presence does not mean
  supported and absence does not mean unsupported; only the step-5 launch test decides.

  Prompted by a circulated list of Electron apps that sorted them by concurrency mode and placed
  Claude and Loom under "single active account only". Both measure as `Honoured` here, and two
  Claude instances have run side by side on the development machine throughout. Such lists
  describe an app's own account-switching UI, not whether it honours `--user-data-dir` — which is
  a different question, and the reason this project exists.

### Earlier in this release

First version that is an application rather than a library. Everything below has been run on
real Windows against real installations of Claude (Microsoft Store), Slack and VS Code.

### Added

- **`Twinstall.exe`** — one binary, four modes: URL routing, `--launch <instance>`,
  `--watch [seconds]`, `--compose`, and a management UI when started with no arguments. The
  routing path dispatches on `argv` before touching a WinForms type, so a sign-in callback
  doesn't pay for the UI.
- **Management UI** — pick an app from presets or Browse, run the full detection pass, name
  instances, choose badge colours, and see exactly what will change before it happens.
  Isolation is enforced as you type, not merely warned about.
- **Protocol registration** via `RegisteredApplications` — a ProgId plus a `UrlAssociations`
  capability, after which *you* pick Twinstall in Settings. It never writes the scheme key
  directly.
- **Badged taskbar icons** — the target app's own logo is extracted from its executable at up
  to 256px and composited with a coloured disc. No third-party artwork ships with Twinstall.
- **Start-menu shortcuts** per instance, since without them there is no way to open the second
  account at all.
- **Detection steps 0–5**, complete: launcher-stub resolution, Chromium confirmation, profile
  root derivation, profile discovery, scheme discovery, and a launch test that proves the app
  honours `--user-data-dir` before anything is committed to.
- `LICENSE` (MIT), `SECURITY.md`, `CHANGELOG.md`.

### Fixed

Each of these was found by running the code against real applications, not by review.

- **`ChromiumDetector` refused VS Code.** It only looked beside the executable; VS Code keeps
  every Chromium file in a commit-hash subfolder. It now sweeps one level of subdirectories.
- **Squirrel launcher stubs failed the launch test.** `%LOCALAPPDATA%\slack\slack.exe` does not
  forward `--user-data-dir`, so Slack was reported as unsupported. `LauncherStub.Resolve`
  redirects to `app-<version>\slack.exe`; the same launch then passes in 1.4s.
- **`ProcessMap` counted unrelated programs as running instances.** It matched on executable
  file name alone, so a separate command-line tool — also `claude.exe`, different path — was reported as
  a running desktop instance. That is enough to turn "nothing is running, ask the user" into a
  confident "only one is running" and route a sign-in to an instance that was never there.
- **Scheme discovery found nothing for already-configured machines.** Keeping only registrations
  whose command references the app misses the case where something else already holds the
  scheme — which is the normal state of a machine set up before. Package-declared schemes are
  now kept regardless of the current holder.
- **`InternalName` was used to identify profiles.** VS Code's is literally `electron`, as is
  most of the ecosystem's; matching it would have selected an unrelated app's profile.
- **Every preset pointed at the wrong executable**, including the one marked verified.
- **`CA5392`** — no P/Invoke pinned its DLL search path, so a `user32.dll` placed beside the
  executable would have been preferred over the real one. All imports now pin System32.
- The launch probe left an empty `%TEMP%\Twinstall` behind.

### Known limits

- **Z-order routing is a heuristic.** Correct when you begin sign-in from the window you are in;
  wrong if you alt-tab mid-flow. Every decision is logged.
- **Per-window taskbar icons need "Combine taskbar buttons: Never"**, a system-wide Windows
  setting that only takes effect after a sign-out. It is offered as an explicit opt-in and is
  never changed silently.
- **Unsigned.** See [SECURITY.md](SECURITY.md) for why antivirus software may object and what
  we will never advise you to do about it.
- Not packaged for the Microsoft Store. Whether certification accepts an app declaring another
  vendor's URL scheme is still untested.

## [0.2.0] — 2026-08-06

### Added

- `Twinstall.Core` — decision logic with no OS dependency, targeting `net8.0` so a Windows call
  fails to compile. Path rules, package-name derivation, command-line parsing, Chromium
  detection, isolation checks, config parsing, route selection.
- `Twinstall.Platform` — thin Win32/WMI/GDI+ adapters holding no decisions.
- `Twinstall.Tests` — a console runner whose exit code is the result. No framework, no restore.