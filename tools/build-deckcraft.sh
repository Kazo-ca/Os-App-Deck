#!/usr/bin/env bash
# Compiles DeckCraft from the external/deckcraft submodule (our fork Kazo-ca/deckcraft, branch
# kazo/v0.18.1; work item 2 of doc/en/plan.md, decision 4) the way its release workflow builds the web zip, then
# vendors the result in src/Kazo.Deck/wwwroot/deckcraft/ with tools/vendor-deckcraft.sh --dir. The
# .github/workflows/build-deckcraft.yml workflow runs it; it also runs on a developer machine.
#
# Usage:
#   tools/build-deckcraft.sh --craft-fonts <folder>
#
# <folder> is a checkout of storytold/craft-fonts at the revision pinned below: DeckCraft's web
# build embeds Inter (regular and bold) and BIZ UDPGothic from it (the browser has no system fonts;
# crates/fonts/build.rs) and refuses to build without them (CRAFT_FONTS_REQUIRED). Needs: Rust stable with the wasm32-unknown-unknown
# target, Trunk at the version below, git, zip and unzip.
set -euo pipefail

# The Trunk version of the Craft release workflow (craft-common-actions, install-trunk), as in
# Kazo Fig's build-figcraft.sh.
trunk_version="0.21.14"
# The craft-fonts revision of DeckCraft's release workflow (.github/workflows/release.yml of the
# v0.18.1 commit, craft-fonts-ref).
craft_fonts_ref="37827adc41fd165f9a9234ac40f94dbad353aa69"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
deckcraft="$repo_root/external/deckcraft"

fail() { echo "error: $*" >&2; exit 1; }

fonts_dir=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --craft-fonts) [[ $# -ge 2 ]] || fail "--craft-fonts needs a folder"; fonts_dir="$2"; shift 2 ;;
    -h|--help) sed -n '2,14p' "$0"; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done
[[ -n "$fonts_dir" ]] || fail "--craft-fonts <folder> is required (a checkout of storytold/craft-fonts at $craft_fonts_ref)"
[[ -d "$fonts_dir" ]] || fail "$fonts_dir is not a folder"
fonts_dir="$(cd "$fonts_dir" && pwd)"
# The fonts embedded in the .wasm must be the release's: the provenance records this revision.
actual_fonts="$(git -C "$fonts_dir" rev-parse HEAD 2>/dev/null)" || fail "$fonts_dir is not a git checkout of craft-fonts"
[[ "$actual_fonts" == "$craft_fonts_ref" ]] || fail "craft-fonts is at $actual_fonts; DeckCraft 0.18.1's release uses $craft_fonts_ref"
[[ -z "$(git -C "$fonts_dir" status --porcelain)" ]] || fail "$fonts_dir has local changes"

[[ -f "$deckcraft/Cargo.toml" ]] || fail "external/deckcraft is empty. Run: git submodule update --init external/deckcraft"
# A local change of DeckCraft must go through a commit of the fork, so the provenance names it.
[[ -z "$(git -C "$deckcraft" status --porcelain)" ]] || fail "external/deckcraft has local changes; commit them in the fork first"

command -v trunk >/dev/null || fail "trunk not found; install Trunk $trunk_version"
actual_trunk="$(trunk --version | awk '{print $2}')"
[[ "$actual_trunk" == "$trunk_version" ]] || fail "trunk $actual_trunk found; DeckCraft's release uses $trunk_version"
rustup target list --installed 2>/dev/null | grep -qx wasm32-unknown-unknown \
  || fail "the wasm32-unknown-unknown target is missing (rustup target add wasm32-unknown-unknown)"
command -v zip >/dev/null && command -v unzip >/dev/null || fail "zip and unzip are needed"

commit="$(git -C "$deckcraft" rev-parse HEAD)"
rustc_version="$(rustc --version)"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# DeckCraft's own packaging script: trunk build --release (wasm size gate included), then the
# licences, NOTICE and font licences next to the site. The build date is the commit's (UTC), so
# building the same commit twice bakes the same date into the About window.
build_date="$(TZ=UTC git -C "$deckcraft" log -1 --format=%cd --date=format-local:%Y-%m-%d)"
echo "Building DeckCraft $commit (rustc: $rustc_version, trunk $actual_trunk, craft-fonts $craft_fonts_ref)"
(
  unset DECKCRAFT_VERSION
  export CRAFT_FONTS_DIR="$fonts_dir" CRAFT_FONTS_REQUIRED=1
  export DECKCRAFT_BUILD_SHA="$commit" DECKCRAFT_BUILD_DATE="$build_date"
  export DIST="$work/dist"
  export CARGO_TARGET_DIR="${CARGO_TARGET_DIR:-$deckcraft/target}"
  cd "$deckcraft"
  packaging/web/package.sh
)

shopt -s nullglob
zips=("$work"/dist/deckcraft-web-*.zip)
shopt -u nullglob
[[ ${#zips[@]} -eq 1 ]] || fail "packaging/web/package.sh made no single deckcraft-web-*.zip"
unzip -q "${zips[0]}" -d "$work/site"
site=("$work"/site/deckcraft-web-*)
[[ ${#site[@]} -eq 1 && -d "${site[0]}" ]] || fail "the zip has no single deckcraft-web-*/ folder"

"$repo_root/tools/vendor-deckcraft.sh" --dir "${site[0]}" \
  --craft-fonts "$craft_fonts_ref" --rustc "$rustc_version" --trunk "$actual_trunk"
