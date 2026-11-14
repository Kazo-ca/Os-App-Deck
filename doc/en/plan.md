> [Version française](../fr/plan.md)

# Plan: the Kazo Deck app for Kazo OS

Step 0 of the Kazo OS [Craft app port guide](https://github.com/Kazo-ca/Os/blob/main/doc/en/wasm/dev/craft-app-port-guide.md),
written on 2026-11-14 (`TODO` issue [#1](https://github.com/Kazo-ca/Os-App-Deck/issues/1)).
This document sets the goal, what reading and trying DeckCraft showed, the decisions, the
questions asked to Benoit and the work items. Each work item is written to be handed as is to an
implementation thread. The closest references are Kazo Photo
([plan](https://github.com/Kazo-ca/Os-App-Photo/blob/photo/base/doc/en/plan.md), the latest port,
several documents per window), Kazo Fig
([plan](https://github.com/Kazo-ca/Os-App-Fig/blob/main/doc/en/plan.md), built from the fork
without changes) and Kazo Pdf
([plan](https://github.com/Kazo-ca/Os-App-PDF/blob/main/doc/en/plan.md), exports received as copies).

## Goal

Kazo Deck is **the presentation app of Kazo OS**: slides, themes, transitions, animations, notes,
slide show, `.pptx` files. It wraps the web build of
[DeckCraft](https://github.com/storytold/deckcraft) (ArtCraft team, `MIT OR Apache-2.0`, Rust and
egui, alpha) and connects it to the files, dialogs and associations of Kazo OS. No Kazo OS app
opens `.pptx` files today.

**What sets this port apart**:

- DeckCraft **already** has the host mode (`craft-web-host`) and the command API
  (`craft-web-api`): no work in the fork before starting, unlike Kazo Photo.
- In host mode, **exports go through the same message as saving** (`deckcraft:save`), not through
  a download: the window has to tell a save from a copy itself.
- The **slide show**: in a web page it fills the page, not the screen, and the **presenter view**
  opens a real browser window. These are the two subjects of this app alone (questions Q2 and Q3).

The shared Kazo code (iframe, proxy, file picker, shortcuts, `DocumentSession`, exports) comes
from the `Kazo.Os.AppSdk.Craft` module of Kazo OS
([ADR 0259](https://github.com/Kazo-ca/Os/blob/main/doc/en/wasm/dev/adr/0259-shared-craft-app-host-module.md),
[ADR 0264](https://github.com/Kazo-ca/Os/blob/main/doc/en/wasm/dev/adr/0264-craft-app-exports-reach-the-window.md),
[guide](https://github.com/Kazo-ca/Os/blob/main/doc/en/wasm/framework/craft-apps.md)). Minimum
version: **Kazo OS 3.3.4**, already released.

## Reading DeckCraft (step 0, 2026-11-14)

Read at the upstream tag `v0.18.1` (commit `7cc73ab1`, head of the `release` branch of
`storytold/deckcraft`, 2026-11-11). The fork [`Kazo-ca/deckcraft`](https://github.com/Kazo-ca/deckcraft)
exists; its `main` (`5e771693`) is a copy of upstream `main` and does not have that commit.

| Point | Finding |
| --- | --- |
| Web build | `apps/deckcraft-web` (Trunk, `public_url = "./"`, `wasm-opt -Oz`), eframe on **wgpu**: WebGPU, falls back to WebGL 2 by itself, `?webgl` forces it. Rust 1.95 at least. Other parameters: `?blank` (empty presentation), `?show` (starts the slide show). |
| Host mode | **Present** (`apps/deckcraft-web/src/host.rs`, `craft-web-host` at revision `fd5e2fe`): `?host=parent` or `data-host="page"` on the canvas. Messages `deckcraft:ready { version: "1", appVersion, file: false }`, `open { name, bytes }`, `save { name, saveAs, bytes }`, `dirty { dirty }`, `failed { error }`. It starts on an empty presentation that the first file received replaces; later ones open beside it. `?author=` signs comments. |
| Command API | **Present in every mode**, with no `control` parameter: `window.deckcraft` (`request`, `execute`, `commands`, `docInspect`, `uiInspect`, `screenshot`). 383 commands. |
| Canvas | `deckcraft_canvas`, hard-coded: one instance per HTML document, so one iframe per window. The page also has `#deckcraft_loading`. |
| Open | `deckcraft:open` with a presentation name (`.pptx .potx .ppsx .deckcraft`): the name becomes the document's file, Save goes back to it. An outline (`.txt .md .rtf`), a file without a name or a damaged file open untitled. File › Open and Insert › Picture, Audio, Video go through the browser picker (`rfd`, `input type=file`): the module's picker replaces them. A file that was **picked or dropped** has no bound file: its first Save is a Save As. |
| Save | `saveAs` is correct, as on the desktop: Save of a file received from the host = its name, `saveAs: false`; a presentation without a file = `<title>.deckcraft`, `saveAs: true`; Save As (`.pptx` or `.deckcraft`) = `true`, then later Saves `false` under that name. |
| Exports | PDF, pictures, outline, GIF, handouts, Print, and a Save As to a type that is not a presentation: **also `deckcraft:save`, with `saveAs: false`**, under a name that is not the document's. The upstream guide says it: "`saveAs: false` doesn't mean safe to overwrite"; only the name the host sent, or the last name saved with `saveAs: true`, is rewritten without asking. One picture per slide = several messages in a row. |
| "Modified" | `deckcraft:dirty` follows all open documents. The document becomes clean **as soon as the bytes are handed over**, before the window has written anything; DeckCraft does not listen to `saved`. This is the pitfall of §6 of the module guide: `DocumentSession` counts a save that did not complete as "modified". |
| Documents | Several presentations per instance, **without tabs**: one moves between them with the `window.next { index }` command (Window › Next Window). `ui.inspect` gives `documents[{ title, dirty, path }]`: no identifier, no active document. `document.inspect` describes the active document (`title`, `path`, `dirty`, `slides`…). `view.newWindow` opens a read-only view of a presentation in the same page. |
| File commands | `file.new`, `file.save`, `file.close`, `file.revert`, `file.openBytes`, `file.saveBytes { format: deckcraft\|pptx }` (returns the bytes as base64). `file.saveAs { path }` and `file.export { path }` as commands answer "not available in the browser": Save As and Export go through DeckCraft's pages (`ui.menu.invoke`). No command renames a document. |
| Slide show | `show.start`, `show.fromStart`, `show.fromCurrent`, `show.end`; `ui.inspect.showing` is an object during the show, `null` otherwise. DeckCraft asks eframe for full screen (`ViewportCommand::Fullscreen`), which the web build **ignores**: the show fills the page, so the iframe, so the Kazo window. Escape ends the show. |
| Presenter view | Setting `presenterView`, **on by default**. On the web it opens, with `window.open`, a **second browser window** on the same page with `#deckcraft-presenter=<token>`, which starts its own instance and talks to the first one over `postMessage` (same origin). When the browser blocks the window, DeckCraft shows an "Open presenter window" button. `show.presenter { on }` turns it on or off. |
| Theme | `ui.set { dark: true\|false }`; `ui.inspect.ui.brightness` is `light` or `dark`. Light by default. |
| Language | **English only**: no catalog, no language setting (`docs/localization-parity.md`). To be written in the Store listing. |
| Settings | **No storage**: neither `localStorage` nor IndexedDB ("preferences are not persisted"). The theme is sent at each start; nothing to keep per user. |
| Fonts | With `CRAFT_FONTS_DIR`, the web build embeds Inter (regular and bold) and BIZ UDPGothic; without it, only egui's fonts. The upstream release pins `craft-fonts` at `37827ad`. No fonts served on demand. A presentation that asks for Calibri or Aptos is rendered with a substitute font. |
| Media | Inserts pictures, sounds (`wav mp3 m4a aac flac ogg opus aif`) and videos (`mp4 m4v mov webm`), decoders in the `.wasm`. Playback in the browser not checked here. |
| Limits | A file of 2 GiB or more is refused by DeckCraft (`failed`); the Kazo OS limit (128 MiB per file) comes well before. |
| Leaving the page | DeckCraft sets no guard in host mode: the close guard of the Kazo window is the only one. |
| Window size | The ribbon is readable at 1280 × 800; the last groups of the Home tab already collapse at that width. |
| Brand | The ArtCraft name and logos are trademarks (`docs/brand/`). Kazo Deck ships the code of the tag unchanged, under its own name and its own icon; "based on DeckCraft by the ArtCraft team" in text is allowed. Same rule as Kazo Fig. |
| Upstream repository rules | `AGENTS.md`: Rust only, no `unwrap`/`panic` outside tests, everything is a command, never break wasm, no CI change in a feature PR, no agent attribution, an issue before a PR. |

### Trial (2026-11-14)

Done with the official `deckcraft-web-0.18.1.zip` of the upstream release
(SHA-256 `3196e537fcfab538aaa2e88584bc74e29bb584459b916ab584ac22fbafe32db5`, 11.9 MB), unchanged,
in Chromium without a graphics card (WebGL 2 through SwiftShader, `?webgl`), behind a test page
that plays the host through `?host=parent`.

| Point | Result |
| --- | --- |
| Content | `index.html`, a 175 KB `.js`, a `.wasm` of **33,159,566 bytes (31.6 MiB)**, licences. Under the Kazo OS limits (128 MiB per file; 64 MiB compressed package). |
| Start | `deckcraft:ready` 1.2 s after the page load, from a local server. Empty presentation "Presentation1", light theme. |
| API | `window.deckcraft` present with no parameter; `ui.inspect` returns `documents: [{ title: "Presentation1", dirty: false, path: null }]`. |
| First save | After a shape is inserted (`dirty: true`), `file.save` sends `save { name: "Presentation1.deckcraft", saveAs: true }`, then `dirty: false` at once, with no answer from the host. |
| Host file | `open { name: "Plan.pptx" }`: a second document `path: "Plan.pptx"` beside the first. Changed, then `file.save`: `save { name: "Plan.pptx", saveAs: false }`. |
| Commands by path | `file.saveAs { path }` and `file.export { path, format }`: "saving to a path is not available in the browser". `ui.menu.invoke file.open`: "use file.openBytes in the browser". |
| Theme | `ui.set { dark: true }`: `brightness: "dark"`. |
| Storage | Empty `localStorage`, no IndexedDB database. |
| Slide show, presenter off | "Present" button or F5: the show fills the iframe; `document.fullscreenElement` stays empty; Escape ends it. |
| Full screen from the page | A page script that sees `showing` become an object and calls `canvas.requestFullscreen()`: **granted** (the click on "Present" counts as the user's gesture), the canvas goes to browser full screen. When full screen ends, the script sends `show.end`: the show ends. The module's iframe already carries `allow="fullscreen"`. |
| Slide show, presenter on | A second browser window opens on `…/index.html?host=parent…#deckcraft-presenter=…`. In that case the full screen request is **refused** ("Permissions check failed"): `window.open` used up the gesture. |
| Downloads | No download and no browser picker came out during the trial. |

Screenshots and scripts of the trial: `deck/trials/` in the project files (outside the repository).

**Not checked at this step**: the build from the fork (the official zip was enough for the trial;
Kazo Fig and Kazo Pdf showed how to do it), the presenter view in the module's page, sound and
video playback, a large `.pptx`, Save As and Export through DeckCraft's pages (read in the code
and the upstream guide, not played).

## Decisions

| # | Subject | Decision |
| --- | --- | --- |
| 1 | Name and identity | "**Kazo Deck**". Id `ca.kazo.deck`, publisher `pub.kazo`, assembly `Kazo.Deck`, category of Kazo Word, several windows, 1280 × 820 window, minimum content 800 × 520. |
| 2 | Shared code | Module `Kazo.Os.AppSdk.Craft`. Submodule `external/KazoOs` on the `v3.3.4` tag, nuget.org packages in Release, `minimumVersion` 3.3.4. |
| 3 | Changing screens | `canChangeScreen: false` in the manifest from work item 2 (issue [#2](https://github.com/Kazo-ca/Os-App-Deck/issues/2), ADR 0263). |
| 4 | Where the build comes from | Built from the fork, **unchanged**: branch `kazo/v0.18.1` of the fork on the commit of the upstream tag (`7cc73ab1`), submodule `external/deckcraft`. `craft-fonts` at the revision of the upstream release (`37827ad`). Trunk's output copied to `wwwroot/deckcraft/` with `deckcraft.json` (version, commit, names and SHA-256), in the repository: the Store does not compile Rust. The official zip is the reference (size compared). `main` of the fork stays the copy of upstream. |
| 5 | Iframe page | `wwwroot/host/index.html`: canvas `deckcraft_canvas` with `data-host="page"`, `data-craft-app="deckcraft"`, `data-craft-build="../deckcraft/deckcraft.json"`, plus `#deckcraft_loading`. No `control` parameter. |
| 6 | Saving and copies | `Saving`: a `save` message whose name is bound in this window, or that carries `saveAs: true`, follows the module rule (`DocumentSession.Decide`). **A `saveAs: false` message whose name is bound to nothing is a copy** (`DecideCopy`): "Save a copy" dialog, the binding does not change. This is the rule upstream wrote, and it covers exports to `.pptx` or `.deckcraft`. `Exporting` (ADR 0264) stays subscribed, as a net: any download link that would still come out becomes a copy. |
| 7 | First save | A presentation without a file is saved as `.deckcraft` (DeckCraft chooses); the OS dialog proposes that name. For a `.pptx`, the user goes through DeckCraft's File › Save As. The window keeps the mapping between the name DeckCraft knows and the path chosen (DeckCraft does not learn the final name). |
| 8 | Binding documents | By the name DeckCraft gives its file (`documents[].path`), the place in the list being the identifier, as in Kazo Photo. A name is bound only once per window: a file whose name is already bound in this window opens in a new window. True whatever the answer to Q1, because DeckCraft's File › New always creates a second document in the same instance. |
| 9 | Close guard | Read from `ui.inspect.documents`; for each modified document, `window.next { index }` then `file.save`, and the window waits for the end of **its own** writes (pitfall of §6 of the guide). |
| 10 | Theme and language | `ui.set { dark }` at `ready` and at each change of the OS theme. No language to send: DeckCraft is in English (limit noted in the listing). |
| 11 | Settings | Nothing to keep per user: no `WebStorageFile` as long as DeckCraft stores nothing. |
| 12 | Shortcuts | `ReservedKeys.Os`, with `Ctrl+N` according to Q1. `F5`, `Shift+F5` and `Escape` go to DeckCraft (slide show). Conflicts between the OS chords and DeckCraft's are listed in work item 3 and noted in the listing. |
| 13 | Associations | Default app (`open` and `edit`) for `.pptx .ppsx .potx .deckcraft`: nothing else opens them in Kazo OS. "Open with" only for `.txt .md .rtf` (DeckCraft turns them into a presentation outline; the text editor stays their default app). Accepted by Benoit on 2026-11-14. |
| 14 | Upstream track | Drafts in English, with no agent mention, in `/mnt/project-files/deck/upstream/`; Benoit publishes them. |
| 15 | Kazo OS ADR | None planned with the recommended answers. If an answer needs one (Q2 = C), next free number **0271 or later**, to be checked again on `main`, the open PRs and every `*/base` branch when writing it. |

## Questions asked to Benoit

Asked in the thread on 2026-11-14. **Benoit answered A to the three questions on 2026-11-14**, and accepted the associations of decision 13: one presentation per window (Q1), real browser full screen for the slide show (Q2), presenter view off in the first version with a `TODO` issue for later (Q3).

**Q1. How many presentations per window?** DeckCraft can hold several presentations in one
instance, but without tabs: the second one is hidden behind Window › Next Window, and the Kazo OS
title bar only shows the active presentation.

- **A (recommended)**: one presentation per window, like PowerPoint and Kazo Word. A double-click
  in the file explorer, File › Open and `Ctrl+N` open a **new Kazo Deck window** (unless the
  window is still empty and untouched: it takes the file). Each presentation has its title bar
  and its taskbar button. Cost: one DeckCraft instance per presentation (a 32 MiB `.wasm` in
  memory each, measured in work item 2). DeckCraft's File › New still creates a hidden document
  in the same window; the window handles it correctly (binding, close guard) without promoting it.
- B: several presentations per window, like Kazo Photo. Less memory, but the user does not see
  that a second presentation is open, for lack of tabs.
- C: like B, plus a Kazo tab bar above DeckCraft. Readable, but it is an interface to write and
  to keep in step with DeckCraft (which does not announce its document changes): one more work
  item.

**Q2. The slide show in full screen.** In a web page, DeckCraft's slide show fills the Kazo
window, not the screen. The trial shows that a small script of the app's page can ask for
browser full screen when the show starts.

- **A (recommended)**: real browser full screen. The script of Kazo Deck's page puts the canvas
  in full screen when the show starts and ends the show when full screen is left (Escape). If
  the browser refuses, the show fills the window. Everything is done in this repository, without
  changing Kazo OS or DeckCraft. Limit: full screen covers the physical screen the browser is
  on, not a Kazo OS "screen".
- B: the show fills the Kazo window, nothing more; the user maximizes the window. The title bar
  and the taskbar stay visible. The simplest, but weak for a presentation app.
- C: a Kazo OS "full screen" window (no frame, above the taskbar). Stays inside the OS and would
  serve other apps, but needs an ADR and a work item in `Kazo-ca/Os`, then a new minimum OS
  version: Kazo Deck would ship later.

**Q3. The presenter view.** DeckCraft turns it on by default and opens a second **browser
window** (notes, next slide, timer), outside Kazo OS. When it opens, the full screen of Q2 = A is
refused by the browser.

- **A (recommended)**: off in the first version. The window turns it off at start and the page
  refuses `window.open`; notes stay visible in Normal view. Limit written in the listing, and a
  `TODO` issue for later.
- B: left as is, in a browser window. This is the real use with a projector (the audience on one
  screen, the notes on the other), but the window leaves Kazo OS, the show is then not in full
  screen automatically, and it must be checked that the module's page starts alone in that
  window.
- C: the presenter in a second Kazo window. Needs a change to DeckCraft in the fork (it talks to
  its window through `window.open`): modified build, trademarks to remove, patch to carry at
  each version. Not for the first version.

## Work items

One working repository, `Kazo-ca/Os-App-Deck`: integration branch `deck/base`, one branch and one
PR per work item, one implementation thread per work item. The fork `Kazo-ca/deckcraft` only
receives the `kazo/v0.18.1` branch. Benoit merges `deck/base` into `main`.

| No. | Branch (to `deck/base`) | Content | Depends on |
| --- | --- | --- | --- |
| 1 | `deck/1-plan` | this plan (fr and en) | — |
| 2 | `deck/2-solution` | solution, manifest, build from the fork, iframe page, window that starts | plan merged; `Kazo-ca/deckcraft` added to the project |
| 3 | `deck/3-window` | open, save, copies, binding, close guard, picker, shortcuts, theme | 2, Q1 |
| 4 | `deck/4-slide-show` | full screen slide show and presenter view | 2, Q2 and Q3; preferably after 3 (same window) |
| 5 | `deck/5-store` | listing, icon, screenshots, README, licences, measures, installed journey, catalog, `features.md` | 3 and 4 |

### 2. Solution and build (`deck/2-solution`)

- Renamed solution (`Kazo.Deck`, `Kazo.Deck.Os`, `Kazo.Deck.Tests`, `tests/e2e`), `[AppManifest]`
  of decision 1, `canChangeScreen: false`, capabilities of Kazo Fig, associations of decision 13.
- Fork: create `kazo/v0.18.1` on `7cc73ab1`
  (`git fetch https://github.com/storytold/deckcraft refs/tags/v0.18.1`), push it, submodule
  `external/deckcraft` on it.
- `tools/build-deckcraft.sh` (current stable Rust, target `wasm32-unknown-unknown`, Trunk,
  `CRAFT_FONTS_DIR` on a clone of `craft-fonts` at `37827ad`, `trunk build --release` in
  `apps/deckcraft-web`), `tools/vendor-deckcraft.sh`, workflow `build-deckcraft.yml`, xUnit test
  "the commit in `deckcraft.json` is the submodule's". Models: `Os-App-Fig`, `Os-App-PDF`.
- Iframe page (decision 5) and window on `CraftFrame` and `FrameStartup`: loading, app, error,
  "Retry"; theme at `ready` (decision 10).
- **Done when**: the app starts in WebGPU and in WebGL 2, two windows run together, the
  `.kazoapp` installs in the released Kazo OS 3.3.4; `.wasm` size compared with the official zip,
  package size and memory per window noted here.
- **Done on 2026-11-14** (PR to `deck/base`). Fork: branch `kazo/v0.18.1` of
  `Kazo-ca/deckcraft` on `7cc73ab1`, nothing else changed; submodule `external/deckcraft` on it,
  submodule `external/KazoOs` on `v3.3.4`. Built by `tools/build-deckcraft.sh` (rustc 1.97.0,
  Trunk 0.21.14, `craft-fonts` `37827ad`, `CRAFT_FONTS_REQUIRED`), DeckCraft's own
  `packaging/web/package.sh` as it is. Checked:
  - `dotnet build -c Release` and `dotnet test -c Release`: 39 tests green (including "the commit of
    `deckcraft.json` is the submodule's" and the size and SHA-256 of every file).
  - `npm run test:e2e` (host `Kazo.Deck.Os` in Release, Chromium without a graphics card): 3 runs
    green: the iframe page with no `control` parameter; DeckCraft on WebGL 2 (`--webgl`), window
    shown at `ready`, no screen left, the OS dark theme received through `ui.set { dark }`, one
    empty presentation; two windows together, each with its own instance (a slide added in one
    does not change the other).
  - `npm run test:installed` (`tests/installed/install-and-start.mjs`, new): the `.kazoapp`
    installs with `sudo kazopkg install` in the **published Kazo OS 3.3.4**
    (`kazoos-wasm-3.3.4.zip`), DeckCraft shows in 4.9 s in the first window (download and compile,
    local server), 1.3 s in the second; with no flag, DeckCraft fell back to WebGL 2 by itself (no
    WebGPU in that Chromium).
  - **Not checked**: **WebGPU** rendering (Chromium's software WebGPU loses its device without a
    graphics card, as for Kazo Fig and Kazo Photo); to try on a machine with a GPU.
  - Debug builds (`KazoOsUseSource=true`) do not compile in this container (.NET SDK 10.0.115,
    Razor errors in the submodule's `Kazo.Os.Apps.Browser`); CI and the Store use the Release
    profile, which is green.

  | Measure | Value |
  | --- | --- |
  | Compiled `.wasm` | **38,745,366 bytes** (36.9 MiB; gzip -9: 15,254,992) |
  | Official zip's `.wasm` | 33,159,566 bytes (31.6 MiB; gzip -9: 11,840,503) |
  | Difference | **+5,585,800 bytes (+16.8%)**: the `craft-fonts` fonts. Inter regular and bold (832,068 bytes) and BIZ UDPGothic (4,669,688 bytes) make 5.50 MB; the difference is the size of these fonts and the official zip has no OFL licence: the upstream release seems built without `CRAFT_FONTS_DIR` (inferred, not checked in its logs). Ours has the fonts the release promises. |
  | Package `ca.kazo.deck-0.1.0.kazoapp` | 15,373,139 bytes (14.7 MiB); 37.3 MiB unpacked, 43 files |
  | Memory per window | Chromium's renderer process (`ps`, software rendering): +347 MiB for the first window, +46 MiB for the second; the GPU process is not counted. A rough measure, to redo on a machine with a GPU. |

  Notes for what follows: `package.sh` copies the OFL licence of **every** `craft-fonts` family (30
  `OFL-*.txt` files) while the web build embeds two; they stay in `wwwroot/deckcraft/` (nothing
  wrong, a few KB too many), and work item 5 takes it into account in `THIRD-PARTY-NOTICES`.
  `Ctrl+N` (a new window, Q1 = A) is not reserved yet: it comes with work item 3's window.

### 3. Window (`deck/3-window`)

- Opening: file asked at launch; DeckCraft's File › Open shows the OS dialog (`PickRequested`
  with the presentation extensions), DeckCraft hears "cancelled" and the window sends
  `deckcraft:open` itself with the name, so that the file is bound; same window or new window
  according to Q1. The other pickers (picture, sound, video) receive the files read.
- Saving and copies: decisions 6 and 7, end to end, including Save As and Export through
  DeckCraft's pages; a cancelled dialog or a refused write leaves the presentation modified,
  with a message. Exports of several files (one picture per slide): note the behaviour and pick
  the least painful form (one dialog per file, or a folder chosen once); ask Benoit if both are
  equal.
- Binding and title (decision 8), close guard (decision 9), shortcuts (decision 12), theme
  followed live.
- Measures: a `.pptx` of a few tens of MB (open and save time, memory); playback of an inserted
  sound and video.
- **Done when**: no case loses changes unless the user chose it, no browser dialog or download
  comes out of the app, an export never overwrites a file without asking, and the OS shortcuts
  work with the cursor in the slide.

- **Done on 2026-11-14** (PR to `deck/base`). What is built:
  - `Documents/DeckDocuments.cs` around the module's `DocumentSession`: list of presentations
    read from `ui.inspect` (`DeckDocumentList`, the place in the list is the index `window.next`
    takes) with the one in front from `document.inspect`; title `*name — Kazo Deck`; "untouched
    blank presentation" (Q1 = A: a file opened from it replaces it, otherwise it opens in a new
    window; a file already open in the window does nothing). An outline (`.txt .md .rtf`)
    becomes an untitled presentation and binds nothing.
  - Saving (decisions 6 and 7): `saveAs: true`, or the name of the presentation in front when it
    is bound, is a save; any other `save` is a **copy** (export, print to PDF): the dialog always
    asks, the binding stays. An export in the format of an earlier file of the presentation
    (`Plan.pptx` after a Save As to `Plan.deckcraft`) is therefore a copy, never a rewrite. A
    cancelled dialog or a refused write leaves the window modified, with a message, and the next
    save asks for a path again.
  - Exports of several files (`CopySeries`): "Export › every slide as pictures" sends
    `<title>1.png`, `<title>2.png`… one after the other. The first file asks where to go, the
    next ones follow it into the same folder under DeckCraft's names (within 5 s of the previous
    copy); a file that already exists asks again. Form chosen by Benoit on 2026-11-14 (rather
    than one dialog per picture or a folder chosen once).
  - Close guard (decision 9): Yes / No / Cancel; Yes saves each modified presentation in turn
    (`window.next { index }` then `file.save`), waiting for each write; a cancelled dialog keeps
    the window open.
  - OS dialogs for Open (Presentations filter), pictures, sounds and videos; `Ctrl+N` reserved,
    opens a new window; theme followed live (`ui.set { dark }`); the OS user's name passed as
    `?author=` (author of comments).
  - Shortcut conflict found: `Alt+F9` (View › Guides in DeckCraft) is an OS chord and does not
    reach DeckCraft; the menu remains. No other conflict seen in `ReservedKeys.Os`.
- Checked:
  - `dotnet test -c Release`: 92 tests green (list of presentations, copy series, save and copy
    decisions, title, `Ctrl+N`, parameters).
  - `npm run test:e2e` (Release, Chromium without a graphics card): 9 runs green, 6 of them new
    (`tests/e2e/open-save.e2e.mjs`): new presentation saved, rewritten then reopened by
    double-click; Save As, print to PDF and export of every slide as pictures with a single
    dialog; close guard (Cancel, Yes then cancelled dialog, Yes with a name); File › Open in the
    blank window then in a new window, and the same file doing nothing; `Ctrl+N`; `.pptx` export,
    opened by double-click, saved in place, closed by `Alt+Q` with no dialog and reopened.

  | Measure (Chromium without a graphics card, local server) | Value |
  | --- | --- |
  | 12.8 MB `.pptx` (8 slides, 3 PNG pictures of 4.2 MB, a sound, a video) | opened by double-click in **11.0 s** (window launch, DeckCraft start, read and open included) |
  | Save in place after a change | **165 ms** |
  | Memory | about +144 MiB for the window and the file (all Chromium processes, rough measure) |
  | Inserted sound and video | `insert.audio` and `insert.video` answer `playable: true`, `media.play` answers ok and the video shows its playback bar (at 00:03.02 in the screenshot); the sound and picture **actually rendered are not checked** without speakers or GPU (decoding in the `.wasm`) |

  The file is 12.8 MB, not the few tens planned (DeckCraft's limit: 128 MiB per file): measures
  to redo with a bigger file, on a machine with a GPU.

### 4. Slide show (`deck/4-slide-show`)

- According to Q2: a page script (`wwwroot/host/`, a file of the app, no inline script) that
  follows `ui.inspect.showing`, asks for full screen within the delay of the user's gesture, ends
  the show when full screen is left, and falls back to "fills the window" when refused. Check
  `F5`, `Shift+F5`, the "Present" button, `?show` and the end by the last slide.
- According to Q3: `show.presenter { on: false }` at `ready` and `window.open` neutralized in the
  page; check that DeckCraft leaves no "Open presenter window" button on screen.
- **Done when**: a show started with a click goes to full screen and leaves it cleanly, and
  nothing opens outside Kazo OS (or according to the answers to Q2 and Q3).
- **Done on 2026-11-14** (PR to `deck/base`). Q2 = A and Q3 = A, with no change to Kazo OS or
  DeckCraft:
  - `wwwroot/host/slide-show.js` (a module loaded by the iframe page, commented): after each
    **real** key press or click in the page (not replayed events), three `uiInspect().showing`
    questions at 60, 200 and 450 ms; a show that just started gets the canvas in full screen
    (`requestFullscreen`), once per show, while the gesture allows it. Leaving full screen (Esc, or
    the browser) sends `show.end`; a show that ends by itself (after the last slide) leaves full
    screen. Refused: the show fills the window. Outside a show, **no timer**: only these three
    questions after a gesture (each costs one DeckCraft frame); during a show, one question every
    500 ms to see it end. The state is written on `<html data-deck-show>` (`full-screen`, `window`).
  - Presenter view: the window sends `engine.execute { command: "show.presenter",
    params: { on: false } }` at `ready` (`DeckCraftShow`; `show.presenter` alone is not a control
    method); the script turns it off again when it sees it on (the Slide Show tab's "Presenter
    View" button) and refuses `window.open` **for the presenter's address only**
    (`#deckcraft-presenter=`), links in slides still open; `deck-page.css` hides the "Open
    presenter window" button DeckCraft puts on `body` if a show still starts with the presenter on.
    Notes stay visible in Normal view.
  - Checked: `dotnet build -c Release` and `dotnet test -c Release` (42 green tests, including the
    page loading the script and the style sheet, the presenter command); `npm run test:unit`
    (10 tests of the script on a fake page: full screen, Esc, end by itself, refusal, one burst per
    gesture, replayed events ignored, no timer outside a show); `npm run test:e2e` (host in
    Release, Chromium without a graphics card, WebGL 2): `F5` goes to full screen without reloading
    the page, Esc ends the show and the focus stays in the Kazo window (`F5` starts again);
    `Shift+F5` starts from the current slide; the "Present" button; the end after the last slide
    leaves full screen; presenter turned on again: no browser window, no visible button, turned off
    again; a show started without a gesture fills the window, then goes to full screen on the next
    click.
  - **Not checked**: `?show` as such (the window never sets it; the "show started without a
    gesture" case covers it); the browser's real Esc in full screen (headless Chromium keeps the
    key: the run leaves full screen with `exitFullscreen`, as the browser does); Firefox and Safari
    (shorter gesture delay in Safari); WebGPU rendering.

### 5. Store (`deck/5-store`)

- Icon without the ArtCraft logo, listing in fr and en ("based on DeckCraft by the ArtCraft
  team"; known limits: alpha, English interface, 128 MiB per file, substitute fonts, presenter
  view, window that stays on its screen), screenshots, README rebuilt from the listing
  (`store:readme`, "Try it" with `?autolaunch=ca.kazo.deck`), `THIRD-PARTY-NOTICES` (DeckCraft,
  its notices, the embedded fonts).
- Journey in the released Kazo OS (`tests/installed/`), "Tag a release" workflow, PR to the
  `Os-AppMarketPlace` catalog (signing happens there, no key here), `features.md` row in
  `Kazo-ca/Os` (fr and en).
- **Done when**: Kazo Deck installs from the Store, opens a `.pptx` on double-click, saves it and
  presents it.

## Upstream track

Drafts in `/mnt/project-files/deck/upstream/deckcraft-web-host.md`, published by Benoit:

- the web build ignores the slide show's full screen request, and the host does not learn that
  a show starts or ends;
- `ui.inspect.documents` has no identifier and no active document;
- the presenter view in host mode (window opened with `window.open`, with no way for the host
  to place it);
- join `craft-libs` #611 (`saved` save confirmation).

Not checked: issues already open upstream on these subjects (`storytold` is out of the session's
reach); Benoit looks before publishing.

## Progress

- [x] Step 0: reading of DeckCraft 0.18.1 and trial of the official web build in host mode.
- [x] Plan in fr and en.
- [x] Benoit's answers to Q1, Q2 and Q3: A everywhere (2026-11-14).
- [x] Work item 2: solution, manifest, build from the fork, iframe page, window (2026-11-14).
- [x] Work item 3: window, opening, saving, copies, close guard (2026-11-14).
- [x] Work item 4: slide show in full screen, presenter view off (2026-11-14).
- [ ] Work item 5: Store.
- [ ] Release: `deck/base` into `main` (Benoit), `v0.1.0` tag, catalog, `features.md`.
