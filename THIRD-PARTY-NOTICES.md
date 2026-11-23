# Third-party notices

> [Version française](THIRD-PARTY-NOTICES-fr.md)

Kazo Deck is under the MIT license ([LICENSE](LICENSE)). Its C# code, its iframe page
(`src/Kazo.Deck/wwwroot/host/index.html`, `deck-page.css`, `slide-show.js`) and its icon
(`src/Kazo.Deck/wwwroot/icons/app.svg`, `store/icon.png`) are this repository's own work. The
iframe proxy it uses (`craft-proxy.js`) belongs to Kazo OS (`Kazo.Os.AppSdk.Craft`, MIT).

It bundles one third-party work, **DeckCraft**, which brings its own third-party material and the
fonts it embeds.

## DeckCraft

| | |
| --- | --- |
| Work | [DeckCraft](https://github.com/storytold/deckcraft), a presentation editor written in Rust, by the ArtCraft team and the DeckCraft contributors |
| Version | 0.19.0, compiled from our fork [`Kazo-ca/deckcraft`](https://github.com/Kazo-ca/deckcraft) (branch `kazo/v0.19.0`, commit `930e1694b2d39ce57cc807ebff572af0e622f618`: the upstream "Release: DeckCraft v0.19.0" commit of the `release` branch, untagged upstream, unchanged) |
| Fonts | embedded at build time from [`storytold/craft-fonts`](https://github.com/storytold/craft-fonts) at commit `37827adc41fd165f9a9234ac40f94dbad353aa69`, as DeckCraft's own releases do |
| License | MIT or Apache-2.0, at the user's option |
| Files | `src/Kazo.Deck/wwwroot/deckcraft/` (the web build: `.js`, `.wasm`, `snippets/`) |
| Provenance | `src/Kazo.Deck/wwwroot/deckcraft/deckcraft.json`: the fork's commit, the craft-fonts commit, the compilers, and the SHA-256 of every file |

The fork holds no change: Kazo Deck runs DeckCraft's own web build, in the host mode DeckCraft
already has.

DeckCraft's own notices ship **unmodified** next to its build, and in the installed package
(`_content/Kazo.Deck/deckcraft/`):

| File | Content |
| --- | --- |
| `LICENSE-MIT`, `LICENSE-APACHE` | DeckCraft's licenses |
| `NOTICE` | DeckCraft's copyright, its trademark notice, and the third-party material it bundles, with the notices those licenses require (Lucide icons, egui fonts and icons, symphonia, krilla…) |
| `OFL-*.txt` | The SIL Open Font License 1.1 of the craft-fonts families, as DeckCraft's web build ships them; that build embeds two of them, Inter and BIZ UDPGothic (`OFL-inter.txt`, `OFL-biz-ud-pgothic.txt`) |

### Files the NOTICE refers to

DeckCraft's `NOTICE` names files and licenses its web build does not carry. Kazo Deck ships them,
unmodified, in `src/Kazo.Deck/wwwroot/legal/deckcraft/`:

| File | Source | Covers |
| --- | --- | --- |
| `ATTRIBUTION.md` | DeckCraft, at the bundled commit | DeckCraft's per-file list of assets (author, source, license) |
| `LICENSE-ICC.txt` | DeckCraft `licenses/pdf-writer/`, at the bundled commit | The Compact ICC Profiles of the PDF export (CC0-1.0) |
| `LICENSE-ubuntu-font.txt` | crate `epaint_default_fonts` 0.37.0, `fonts/UFL.txt` | Ubuntu Light, the interface's last-resort face (Ubuntu Font Licence 1.0) |
| `LICENSE-hack.txt` | crate `epaint_default_fonts` 0.37.0, `fonts/Hack-Regular.txt` | Hack Regular (MIT, Bitstream Vera) |
| `OFL-noto-emoji.txt` | crate `epaint_default_fonts` 0.37.0, `fonts/OFL.txt` | Noto Emoji (SIL Open Font License 1.1) |
| `LICENSE-emoji-icon-font.txt` | crate `epaint_default_fonts` 0.37.0, `fonts/emoji-icon-font-mit-license.txt` | emoji-icon-font and its egui-icons subset (MIT) |
| `LICENSE-symphonia-MPL-2.0.txt` | [Symphonia](https://github.com/pdeljanov/Symphonia) `LICENSE` at tag `v0.5.5` | symphonia 0.5.5, the audio decoder, used unmodified under the Mozilla Public License 2.0; its source is at https://github.com/pdeljanov/Symphonia |

### The ArtCraft brand

The ArtCraft name, wordmark and logos are trademarks of the ArtCraft team; DeckCraft's open-source
license does not cover them. Kazo Deck uses none of them: its name, its icon and its Store listing
are this repository's own, and in Kazo OS DeckCraft runs in host mode. "Based on DeckCraft by the
ArtCraft team" only says where the software comes from.

## In the package

The Kazo OS package (`.kazoapp`) carries, under `wwwroot/`:

- `legal/LICENSE.txt`: Kazo Deck's MIT license (this repository's `LICENSE`);
- `legal/THIRD-PARTY-NOTICES.md` and `legal/THIRD-PARTY-NOTICES-fr.md`: this file and its French
  version;
- `legal/deckcraft/`: the files DeckCraft's `NOTICE` refers to, as above;
- `deckcraft/LICENSE-MIT`, `LICENSE-APACHE`, `NOTICE` and `OFL-*.txt`: DeckCraft's notices, as
  above.

A C# test (`LegalFilesTests`) checks that the project puts these files in the package.

## Test data

The end-to-end tests make their presentations with DeckCraft's own commands and starter templates;
they ship no third-party file.
