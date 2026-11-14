# Avis de tiers

> [English version](THIRD-PARTY-NOTICES.md)

Kazo Deck est sous licence MIT ([LICENSE](LICENSE)). Son code C#, sa page d'iframe
(`src/Kazo.Deck/wwwroot/host/index.html`, `deck-page.css`, `slide-show.js`) et son icône
(`src/Kazo.Deck/wwwroot/icons/app.svg`, `store/icon.png`) sont l'œuvre de ce dépôt. Le proxy
d'iframe qu'il utilise (`craft-proxy.js`) appartient à Kazo OS (`Kazo.Os.AppSdk.Craft`, MIT).

Il intègre une œuvre de tiers, **DeckCraft**, qui apporte son propre matériel de tiers et les
polices qu'il intègre.

## DeckCraft

| | |
| --- | --- |
| Œuvre | [DeckCraft](https://github.com/storytold/deckcraft), un éditeur de présentations écrit en Rust, de l'équipe ArtCraft et des contributeurs de DeckCraft |
| Version | 0.18.1, compilée depuis notre fork [`Kazo-ca/deckcraft`](https://github.com/Kazo-ca/deckcraft) (branche `kazo/v0.18.1`, commit `7cc73ab1895ed2e30c8afbcada20c70b9ea0a440` : l'étiquette amont `v0.18.1`, sans modification) |
| Polices | intégrées à la compilation depuis [`storytold/craft-fonts`](https://github.com/storytold/craft-fonts) au commit `37827adc41fd165f9a9234ac40f94dbad353aa69`, comme le font les versions officielles de DeckCraft |
| Licence | MIT ou Apache-2.0, au choix de l'utilisateur |
| Fichiers | `src/Kazo.Deck/wwwroot/deckcraft/` (le build web : `.js`, `.wasm`, `snippets/`) |
| Provenance | `src/Kazo.Deck/wwwroot/deckcraft/deckcraft.json` : le commit du fork, celui de craft-fonts, les compilateurs, et le SHA-256 de chaque fichier |

Le fork ne contient aucune modification : Kazo Deck utilise le build web de DeckCraft tel quel,
dans le mode hébergé que DeckCraft a déjà.

Les avis de DeckCraft sont livrés **sans modification** à côté de son build, et dans le paquet
installé (`_content/Kazo.Deck/deckcraft/`) :

| Fichier | Contenu |
| --- | --- |
| `LICENSE-MIT`, `LICENSE-APACHE` | Les licences de DeckCraft |
| `NOTICE` | Le copyright de DeckCraft, son avis sur les marques, et le matériel de tiers qu'il intègre, avec les avis que ces licences demandent (icônes Lucide, polices et icônes d'egui, symphonia, krilla…) |
| `OFL-*.txt` | La SIL Open Font License 1.1 des familles de craft-fonts, telles que le build web de DeckCraft les livre ; ce build en intègre deux, Inter et BIZ UDPGothic (`OFL-inter.txt`, `OFL-biz-ud-pgothic.txt`) |

### Fichiers dont parle le NOTICE

Le `NOTICE` de DeckCraft nomme des fichiers et des licences que son build web ne contient pas.
Kazo Deck les livre, sans modification, dans `src/Kazo.Deck/wwwroot/legal/deckcraft/` :

| Fichier | Source | Couvre |
| --- | --- | --- |
| `ATTRIBUTION.md` | DeckCraft, au commit intégré | La liste par fichier des ressources de DeckCraft (auteur, source, licence) |
| `LICENSE-ICC.txt` | `licenses/pdf-writer/` de DeckCraft, au commit intégré | Les profils Compact ICC de l'export PDF (CC0-1.0) |
| `LICENSE-ubuntu-font.txt` | crate `epaint_default_fonts` 0.37.0, `fonts/UFL.txt` | Ubuntu Light, la police de dernier recours de l'interface (Ubuntu Font Licence 1.0) |
| `LICENSE-hack.txt` | crate `epaint_default_fonts` 0.37.0, `fonts/Hack-Regular.txt` | Hack Regular (MIT, Bitstream Vera) |
| `OFL-noto-emoji.txt` | crate `epaint_default_fonts` 0.37.0, `fonts/OFL.txt` | Noto Emoji (SIL Open Font License 1.1) |
| `LICENSE-emoji-icon-font.txt` | crate `epaint_default_fonts` 0.37.0, `fonts/emoji-icon-font-mit-license.txt` | emoji-icon-font et son sous-ensemble egui-icons (MIT) |
| `LICENSE-symphonia-MPL-2.0.txt` | `LICENSE` de [Symphonia](https://github.com/pdeljanov/Symphonia) à l'étiquette `v0.5.5` | symphonia 0.5.5, le décodeur audio, utilisé sans modification sous Mozilla Public License 2.0 ; sa source est à https://github.com/pdeljanov/Symphonia |

### La marque ArtCraft

Le nom, le logotype et les logos ArtCraft sont des marques de l'équipe ArtCraft ; la licence libre
de DeckCraft ne les couvre pas. Kazo Deck n'en utilise aucun : son nom, son icône et sa fiche du
Store sont propres à ce dépôt, et dans Kazo OS DeckCraft tourne en mode hébergé. « Basé sur
DeckCraft de l'équipe ArtCraft » dit seulement d'où vient le logiciel.

## Dans le paquet

Le paquet Kazo OS (`.kazoapp`) contient, sous `wwwroot/` :

- `legal/LICENSE.txt` : la licence MIT de Kazo Deck (le `LICENSE` de ce dépôt) ;
- `legal/THIRD-PARTY-NOTICES.md` et `legal/THIRD-PARTY-NOTICES-fr.md` : la version anglaise et ce
  fichier ;
- `legal/deckcraft/` : les fichiers dont parle le `NOTICE` de DeckCraft, comme ci-dessus ;
- `deckcraft/LICENSE-MIT`, `LICENSE-APACHE`, `NOTICE` et `OFL-*.txt` : les avis de DeckCraft,
  comme ci-dessus.

Un test C# (`LegalFilesTests`) vérifie que le projet met ces fichiers dans le paquet.

## Données de test

Les tests de bout en bout fabriquent leurs présentations avec les commandes et les modèles de
départ de DeckCraft ; ils ne livrent aucun fichier de tiers.
