> [English version](../en/plan.md)

# Plan : l'app Kazo Deck pour Kazo OS

Étape 0 du [guide de portage des apps Craft](https://github.com/Kazo-ca/Os/blob/main/doc/fr/wasm/dev/craft-app-port-guide.md)
de Kazo OS, écrite le 2026-11-14 (issue `TODO` [#1](https://github.com/Kazo-ca/Os-App-Deck/issues/1)).
Ce document fixe le but, ce que la lecture et l'essai de DeckCraft ont montré, les décisions, les
questions posées à Benoit et les chantiers. Chaque chantier est écrit pour être confié tel quel à
un fil d'implémentation. Les références les plus proches sont Kazo Photo
([plan](https://github.com/Kazo-ca/Os-App-Photo/blob/photo/base/doc/fr/plan.md), dernier portage
fait, plusieurs documents par fenêtre), Kazo Fig
([plan](https://github.com/Kazo-ca/Os-App-Fig/blob/main/doc/fr/plan.md), compilé depuis le fork
sans modification) et Kazo Pdf
([plan](https://github.com/Kazo-ca/Os-App-PDF/blob/main/doc/fr/plan.md), exports reçus comme copies).

## But

Kazo Deck est **l'app de présentations de Kazo OS** : diapositives, thèmes, transitions,
animations, notes, diaporama, fichiers `.pptx`. Il emballe le build web de
[DeckCraft](https://github.com/storytold/deckcraft) (équipe ArtCraft, `MIT OR Apache-2.0`, Rust et
egui, en alpha) et le branche sur les fichiers, les dialogues et les associations de Kazo OS.
Aucune app de Kazo OS n'ouvre aujourd'hui les `.pptx`.

**Ce qui distingue ce portage des autres** :

- DeckCraft a **déjà** le mode hébergé (`craft-web-host`) et l'API de commandes
  (`craft-web-api`) : pas de travail dans le fork avant de commencer, contrairement à Kazo Photo.
- En mode hébergé, **les exports passent par le même message que l'enregistrement**
  (`deckcraft:save`), pas par un téléchargement : la fenêtre doit distinguer un enregistrement
  d'une copie elle-même.
- Le **diaporama** : dans une page web il remplit la page, pas l'écran, et le **mode
  présentateur** ouvre une vraie fenêtre du navigateur. Ce sont les deux sujets propres à cette
  app (questions Q2 et Q3).

Le code Kazo commun (iframe, proxy, sélecteur de fichiers, raccourcis, `DocumentSession`,
exports) vient du module `Kazo.Os.AppSdk.Craft` de Kazo OS
([ADR 0259](https://github.com/Kazo-ca/Os/blob/main/doc/fr/wasm/dev/adr/0259-shared-craft-app-host-module.md),
[ADR 0264](https://github.com/Kazo-ca/Os/blob/main/doc/fr/wasm/dev/adr/0264-craft-app-exports-reach-the-window.md),
[guide](https://github.com/Kazo-ca/Os/blob/main/doc/fr/wasm/framework/craft-apps.md)). Version
minimale : **Kazo OS 3.3.4**, déjà publiée.

## Lecture de DeckCraft (étape 0, 2026-11-14)

Lue à l'étiquette amont `v0.18.1` (commit `7cc73ab1`, tête de la branche `release` de
`storytold/deckcraft`, 2026-11-11). Le fork [`Kazo-ca/deckcraft`](https://github.com/Kazo-ca/deckcraft)
existe ; son `main` (`5e771693`) est une copie de `main` amont et n'a pas ce commit.

| Point | Constat |
| --- | --- |
| Build web | `apps/deckcraft-web` (Trunk, `public_url = "./"`, `wasm-opt -Oz`), eframe sur **wgpu** : WebGPU, repli WebGL 2 tout seul, `?webgl` pour le forcer. Rust 1.95 au minimum. Autres paramètres : `?blank` (présentation vide), `?show` (démarre le diaporama). |
| Mode hébergé | **Présent** (`apps/deckcraft-web/src/host.rs`, `craft-web-host` à la révision `fd5e2fe`) : `?host=parent` ou `data-host="page"` sur le canevas. Messages `deckcraft:ready { version: "1", appVersion, file: false }`, `open { name, bytes }`, `save { name, saveAs, bytes }`, `dirty { dirty }`, `failed { error }`. Il démarre sur une présentation vide que le premier fichier reçu remplace ; les suivants s'ouvrent à côté. `?author=` signe les commentaires. |
| API de commandes | **Présente dans tous les modes**, sans paramètre `control` : `window.deckcraft` (`request`, `execute`, `commands`, `docInspect`, `uiInspect`, `screenshot`). 383 commandes. |
| Canevas | `deckcraft_canvas`, écrit en dur : une instance par document HTML, donc une iframe par fenêtre. La page porte aussi `#deckcraft_loading`. |
| Ouvrir | `deckcraft:open` avec un nom de présentation (`.pptx .potx .ppsx .deckcraft`) : le nom devient le fichier du document, Enregistrer y retourne. Un plan (`.txt .md .rtf`), un fichier sans nom ou un fichier abîmé s'ouvrent sans titre. Fichier › Ouvrir et Insertion › Image, Audio, Vidéo passent par le sélecteur du navigateur (`rfd`, `input type=file`) : le sélecteur du module les remplace. Un fichier **choisi ou déposé** n'a pas de fichier lié : son premier Enregistrer est un Enregistrer sous. |
| Enregistrer | `saveAs` est correct, comme sur le bureau : Enregistrer d'un fichier reçu de l'hôte = son nom, `saveAs: false` ; présentation sans fichier = `<titre>.deckcraft`, `saveAs: true` ; Enregistrer sous (`.pptx` ou `.deckcraft`) = `true`, puis les Enregistrer suivants `false` sous ce nom. |
| Exports | PDF, images, plan, GIF, documents à distribuer, Imprimer, et un Enregistrer sous dans un type qui n'est pas une présentation : **aussi `deckcraft:save`, avec `saveAs: false`**, sous un nom qui n'est pas celui du document. Le guide amont le dit : « `saveAs: false` ne veut pas dire sûr à écraser » ; on ne réécrit sans demander que le nom envoyé par l'hôte ou le dernier nom enregistré avec `saveAs: true`. Une image par diapositive = plusieurs messages de suite. |
| « Modifié » | `deckcraft:dirty` suit tous les documents ouverts. Le document redevient propre **dès que les octets sont remis**, avant que la fenêtre ait écrit quoi que ce soit ; DeckCraft n'écoute pas `saved`. C'est le piège du §6 du guide du module : `DocumentSession` compte un enregistrement non abouti comme « modifié ». |
| Documents | Plusieurs présentations par instance, **sans onglets** : on passe de l'une à l'autre par la commande `window.next { index }` (Fenêtre › Fenêtre suivante). `ui.inspect` donne `documents[{ title, dirty, path }]` : ni identifiant, ni document actif. `document.inspect` décrit le document actif (`title`, `path`, `dirty`, `slides`…). `view.newWindow` ouvre une vue en lecture d'une présentation dans la même page. |
| Commandes de fichier | `file.new`, `file.save`, `file.close`, `file.revert`, `file.openBytes`, `file.saveBytes { format: deckcraft\|pptx }` (rend les octets en base64). `file.saveAs { path }` et `file.export { path }` par commande répondent « non disponible dans le navigateur » : Enregistrer sous et Exporter passent par les pages de DeckCraft (`ui.menu.invoke`). Pas de commande pour renommer un document. |
| Diaporama | `show.start`, `show.fromStart`, `show.fromCurrent`, `show.end` ; `ui.inspect.showing` vaut un objet pendant le diaporama, `null` sinon. DeckCraft demande le plein écran à eframe (`ViewportCommand::Fullscreen`), que le build web **ignore** : le diaporama remplit la page, donc l'iframe, donc la fenêtre Kazo. Échap termine le diaporama. |
| Mode présentateur | Réglage `presenterView`, **actif par défaut**. Sur le web il ouvre par `window.open` une **deuxième fenêtre du navigateur** sur la même page avec `#deckcraft-presenter=<jeton>`, qui démarre sa propre instance et parle à la première par `postMessage` (même origine). Si le navigateur bloque la fenêtre, DeckCraft montre un bouton « Open presenter window ». `show.presenter { on }` l'active ou le coupe. |
| Thème | `ui.set { dark: true\|false }` ; `ui.inspect.ui.brightness` vaut `light` ou `dark`. Clair par défaut. |
| Langue | **Anglais seulement** : pas de catalogue, pas de réglage de langue (`docs/localization-parity.md`). À écrire dans la fiche. |
| Réglages | **Aucun stockage** : ni `localStorage` ni IndexedDB (« preferences are not persisted »). Le thème est renvoyé à chaque démarrage ; rien à garder par utilisateur. |
| Polices | Avec `CRAFT_FONTS_DIR`, le build web intègre Inter (normal et gras) et BIZ UDPGothic ; sans, seulement les polices d'egui. La release amont épingle `craft-fonts` à `37827ad`. Pas de polices servies à la demande. Une présentation qui demande Calibri ou Aptos est rendue avec une police de remplacement. |
| Médias | Insertion d'images, de sons (`wav mp3 m4a aac flac ogg opus aif`) et de vidéos (`mp4 m4v mov webm`), décodeurs dans le `.wasm`. Lecture dans le navigateur non vérifiée ici. |
| Limites | Fichier de 2 Gio ou plus refusé par DeckCraft (`failed`) ; la limite de Kazo OS (128 Mio par fichier) passe bien avant. |
| Fermeture de page | DeckCraft ne pose pas de garde en mode hébergé : la garde de fermeture de la fenêtre Kazo est la seule. |
| Taille de fenêtre | Le ruban est lisible à 1280 × 800 ; les derniers groupes de l'onglet Accueil se replient déjà à cette largeur. |
| Marque | Le nom et les logos ArtCraft sont des marques (`docs/brand/`). Kazo Deck livre le code de l'étiquette sans changement, sous son propre nom et sa propre icône ; « basé sur DeckCraft de l'équipe ArtCraft » en texte est permis. Même règle que Kazo Fig. |
| Règles du dépôt amont | `AGENTS.md` : Rust seulement, pas de `unwrap`/`panic` hors tests, tout est une commande, ne jamais casser wasm, pas de changement de CI dans une PR de fonctionnalité, pas de mention d'agent, une issue avant une PR. |

### Essai (2026-11-14)

Fait avec le zip officiel `deckcraft-web-0.18.1.zip` de la release amont
(SHA-256 `3196e537fcfab538aaa2e88584bc74e29bb584459b916ab584ac22fbafe32db5`, 11,9 Mo), sans aucun
changement, dans Chromium sans carte graphique (WebGL 2 par SwiftShader, `?webgl`), derrière une
page d'essai qui joue l'hôte par `?host=parent`.

| Point | Résultat |
| --- | --- |
| Contenu | `index.html`, un `.js` de 175 Ko, un `.wasm` de **33 159 566 octets (31,6 Mio)**, licences. Sous les limites de Kazo OS (128 Mio par fichier ; paquet de 64 Mio compressé). |
| Démarrage | `deckcraft:ready` 1,2 s après le chargement de la page, depuis un serveur local. Présentation vide « Presentation1 », thème clair. |
| API | `window.deckcraft` présent sans paramètre ; `ui.inspect` rend `documents: [{ title: "Presentation1", dirty: false, path: null }]`. |
| Premier enregistrement | Après une forme insérée (`dirty: true`), `file.save` envoie `save { name: "Presentation1.deckcraft", saveAs: true }`, puis `dirty: false` tout de suite, sans réponse de l'hôte. |
| Fichier de l'hôte | `open { name: "Plan.pptx" }` : un deuxième document `path: "Plan.pptx"` à côté du premier. Modifié puis `file.save` : `save { name: "Plan.pptx", saveAs: false }`. |
| Commandes par chemin | `file.saveAs { path }` et `file.export { path, format }` : « saving to a path is not available in the browser ». `ui.menu.invoke file.open` : « use file.openBytes in the browser ». |
| Thème | `ui.set { dark: true }` : `brightness: "dark"`. |
| Stockage | `localStorage` vide, aucune base IndexedDB. |
| Diaporama, présentateur coupé | Bouton « Present » ou F5 : le diaporama remplit l'iframe ; `document.fullscreenElement` reste vide ; Échap le termine. |
| Plein écran par la page | Un script de la page qui voit `showing` passer à un objet et appelle `canvas.requestFullscreen()` : **accordé** (le clic sur « Present » compte comme geste de l'utilisateur), le canevas passe en plein écran du navigateur. À la sortie du plein écran, le script envoie `show.end` : le diaporama se termine. L'iframe du module porte déjà `allow="fullscreen"`. |
| Diaporama, présentateur actif | Une deuxième fenêtre du navigateur s'ouvre sur `…/index.html?host=parent…#deckcraft-presenter=…`. Dans ce cas la demande de plein écran est **refusée** (« Permissions check failed ») : `window.open` a consommé le geste. |
| Téléchargements | Aucun téléchargement ni sélecteur du navigateur n'est sorti pendant l'essai. |

Captures et scripts de l'essai : `deck/trials/` dans les fichiers du projet (hors dépôt).

**Non vérifié à cette étape** : la compilation depuis le fork (le zip officiel a suffi pour
l'essai ; Kazo Fig et Kazo Pdf ont montré la marche à suivre), le mode présentateur dans la page
du module, la lecture des sons et des vidéos, un grand `.pptx`, Enregistrer sous et Exporter par
les pages de DeckCraft (lus dans le code et le guide amont, pas joués).

## Décisions

| # | Sujet | Décision |
| --- | --- | --- |
| 1 | Nom et identité | « **Kazo Deck** ». Id `ca.kazo.deck`, éditeur `pub.kazo`, assembly `Kazo.Deck`, catégorie de Kazo Word, plusieurs fenêtres, fenêtre de 1280 × 820, contenu minimum 800 × 520. |
| 2 | Code commun | Module `Kazo.Os.AppSdk.Craft`. Sous-module `external/KazoOs` sur l'étiquette `v3.3.4`, paquets de nuget.org en Release, `minimumVersion` 3.3.4. |
| 3 | Changement d'écran | `canChangeScreen: false` dans le manifeste dès le chantier 2 (issue [#2](https://github.com/Kazo-ca/Os-App-Deck/issues/2), ADR 0263). |
| 4 | Provenance du build | Compilé depuis le fork, **sans modification** : branche `kazo/v0.18.1` du fork sur le commit de l'étiquette amont (`7cc73ab1`), sous-module `external/deckcraft`. `craft-fonts` à la révision de la release amont (`37827ad`). Sortie de Trunk copiée dans `wwwroot/deckcraft/` avec `deckcraft.json` (version, commit, noms et SHA-256), dans le dépôt : le Store ne compile pas de Rust. Le zip officiel sert de témoin (taille comparée). `main` du fork reste la copie de l'amont. |
| 5 | Page de l'iframe | `wwwroot/host/index.html` : canevas `deckcraft_canvas` avec `data-host="page"`, `data-craft-app="deckcraft"`, `data-craft-build="../deckcraft/deckcraft.json"`, plus `#deckcraft_loading`. Aucun paramètre `control`. |
| 6 | Enregistrement et copies | `Saving` : un message `save` dont le nom est lié dans cette fenêtre, ou qui porte `saveAs: true`, suit la règle du module (`DocumentSession.Decide`). **Un message `saveAs: false` dont le nom n'est lié à rien est une copie** (`DecideCopy`) : dialogue « Enregistrer une copie », la liaison ne change pas. C'est la règle écrite par l'amont, et elle couvre les exports en `.pptx` ou `.deckcraft`. `Exporting` (ADR 0264) reste abonné, en filet : tout lien de téléchargement qui sortirait quand même devient une copie. |
| 7 | Premier enregistrement | Une présentation sans fichier s'enregistre en `.deckcraft` (c'est DeckCraft qui choisit) ; le dialogue de l'OS propose ce nom. Pour un `.pptx`, l'utilisateur passe par Fichier › Enregistrer sous de DeckCraft. La fenêtre garde la correspondance entre le nom que DeckCraft connaît et le chemin choisi (DeckCraft n'apprend pas le nom final). |
| 8 | Liaison des documents | Par le nom que DeckCraft donne à son fichier (`documents[].path`), la place dans la liste servant d'identifiant, comme Kazo Photo. Un nom n'est lié qu'une fois par fenêtre : un fichier dont le nom est déjà lié dans cette fenêtre s'ouvre dans une nouvelle fenêtre. Vrai quelle que soit la réponse à Q1, parce que Fichier › Nouveau de DeckCraft crée toujours un deuxième document dans la même instance. |
| 9 | Garde de fermeture | Lue dans `ui.inspect.documents` ; pour chaque document modifié, `window.next { index }` puis `file.save`, et la fenêtre attend la fin de **ses** écritures (piège du §6 du guide). |
| 10 | Thème et langue | `ui.set { dark }` à `ready` et à chaque changement de thème de l'OS. Pas de langue à envoyer : DeckCraft est en anglais (limite notée dans la fiche). |
| 11 | Réglages | Rien à garder par utilisateur : pas de `WebStorageFile` tant que DeckCraft ne stocke rien. |
| 12 | Raccourcis | `ReservedKeys.Os`, avec `Ctrl+N` selon Q1. `F5`, `Maj+F5` et `Échap` vont à DeckCraft (diaporama). Les conflits entre les accords de l'OS et ceux de DeckCraft sont relevés au chantier 3 et notés dans la fiche. |
| 13 | Associations | App par défaut (`open` et `edit`) pour `.pptx .ppsx .potx .deckcraft` : rien d'autre ne les ouvre dans Kazo OS. « Ouvrir avec » seulement pour `.txt .md .rtf` (DeckCraft en fait un plan de présentation ; l'éditeur de texte reste leur app par défaut). Acceptée par Benoit le 2026-11-14. |
| 14 | Piste amont | Brouillons en anglais, sans mention d'agent, dans `/mnt/project-files/deck/upstream/` ; Benoit les publie. |
| 15 | ADR de Kazo OS | Aucune prévue avec les réponses recommandées. Si une réponse en demande une (Q2 = C), prochain numéro libre **0271 ou suivant**, à revérifier sur `main`, les PR ouvertes et toutes les branches `*/base` au moment de l'écrire. |

## Questions posées à Benoit

Posées dans le fil le 2026-11-14. **Benoit a répondu A aux trois questions le 2026-11-14**, et a accepté les associations de la décision 13 : une présentation par fenêtre (Q1), vrai plein écran du navigateur pour le diaporama (Q2), mode présentateur coupé dans la première version avec une issue `TODO` pour la suite (Q3).

**Q1. Combien de présentations par fenêtre ?** DeckCraft peut tenir plusieurs présentations
dans une instance, mais sans onglets : la deuxième est cachée derrière Fenêtre › Fenêtre
suivante, et la barre de titre de Kazo OS ne montre que la présentation active.

- **A (recommandée)** : une présentation par fenêtre, comme PowerPoint et Kazo Word. Un
  double-clic dans l'explorateur, Fichier › Ouvrir et `Ctrl+N` ouvrent une **nouvelle fenêtre
  Kazo Deck** (sauf si la fenêtre est encore vide et intacte : elle prend le fichier). Chaque
  présentation a sa barre de titre et son bouton dans la barre des tâches. Coût : une instance
  de DeckCraft par présentation (un `.wasm` de 32 Mio en mémoire chacune, mesure au chantier 2).
  Fichier › Nouveau de DeckCraft crée encore un document caché dans la même fenêtre ; la fenêtre
  le gère correctement (liaison, garde de fermeture), sans le mettre en avant.
- B : plusieurs présentations par fenêtre, comme Kazo Photo. Moins de mémoire, mais l'utilisateur
  ne voit pas qu'une deuxième présentation est ouverte, faute d'onglets.
- C : comme B, plus une barre d'onglets Kazo au-dessus de DeckCraft. Lisible, mais c'est une
  interface à écrire et à tenir en accord avec DeckCraft (qui n'annonce pas ses changements de
  document) : un chantier de plus.

**Q2. Le diaporama en plein écran.** Dans une page web, le diaporama de DeckCraft remplit la
fenêtre Kazo, pas l'écran. L'essai montre qu'un petit script de la page de l'app peut demander
le plein écran du navigateur au démarrage du diaporama.

- **A (recommandée)** : vrai plein écran du navigateur. Le script de la page de Kazo Deck passe
  le canevas en plein écran quand le diaporama démarre et termine le diaporama quand on en sort
  (Échap). Si le navigateur refuse, le diaporama remplit la fenêtre. Tout se fait dans ce dépôt,
  sans changer Kazo OS ni DeckCraft. Limite : le plein écran couvre l'écran physique où se
  trouve le navigateur, pas un « écran » de Kazo OS.
- B : le diaporama remplit la fenêtre Kazo, rien de plus ; l'utilisateur agrandit la fenêtre.
  La barre de titre et la barre des tâches restent visibles. Le plus simple, mais faible pour
  une app de présentations.
- C : une fenêtre « plein écran » de Kazo OS (sans cadre, par-dessus la barre des tâches). Reste
  dans l'OS et servirait à d'autres apps, mais demande une ADR et un chantier dans `Kazo-ca/Os`,
  puis une nouvelle version minimale de l'OS : Kazo Deck sortirait plus tard.

**Q3. Le mode présentateur.** DeckCraft l'active par défaut et ouvre une deuxième **fenêtre du
navigateur** (notes, diapositive suivante, minuteur), en dehors de Kazo OS. Quand elle s'ouvre,
le plein écran de Q2 = A est refusé par le navigateur.

- **A (recommandée)** : coupé dans la première version. La fenêtre le désactive au démarrage et
  la page refuse `window.open` ; les notes restent visibles en mode Normal. Limite écrite dans la
  fiche, et une issue `TODO` pour la suite.
- B : on le laisse tel quel, dans une fenêtre du navigateur. C'est l'usage réel avec un
  projecteur (le public sur un écran, les notes sur l'autre), mais la fenêtre sort de Kazo OS,
  le diaporama n'est alors pas en plein écran automatiquement, et il faut vérifier que la page
  du module démarre seule dans cette fenêtre.
- C : le présentateur dans une deuxième fenêtre Kazo. Demande de modifier DeckCraft dans le fork
  (il parle à sa fenêtre par `window.open`) : build modifié, marques à retirer, patch à
  reporter à chaque version. Pas pour la première version.

## Chantiers

Un dépôt de travail, `Kazo-ca/Os-App-Deck` : branche d'intégration `deck/base`, une branche et une
PR par chantier, un fil d'implémentation par chantier. Le fork `Kazo-ca/deckcraft` ne reçoit que la
branche `kazo/v0.18.1`. Benoit fusionne `deck/base` dans `main`.

| N° | Branche (vers `deck/base`) | Contenu | Dépend de |
| --- | --- | --- | --- |
| 1 | `deck/1-plan` | ce plan (fr et en) | — |
| 2 | `deck/2-solution` | solution, manifeste, compilation depuis le fork, page d'iframe, fenêtre qui démarre | plan fusionné ; `Kazo-ca/deckcraft` ajouté au projet |
| 3 | `deck/3-window` | ouvrir, enregistrer, copies, liaison, garde de fermeture, sélecteur, raccourcis, thème | 2, Q1 |
| 4 | `deck/4-slide-show` | plein écran du diaporama et mode présentateur | 2, Q2 et Q3 ; après 3 de préférence (même fenêtre) |
| 5 | `deck/5-store` | fiche, icône, captures, README, licences, mesures, parcours installé, catalogue, `features.md` | 3 et 4 |

### 2. Solution et compilation (`deck/2-solution`)

- Solution renommée (`Kazo.Deck`, `Kazo.Deck.Os`, `Kazo.Deck.Tests`, `tests/e2e`), `[AppManifest]`
  de la décision 1, `canChangeScreen: false`, capacités de Kazo Fig, associations de la décision 13.
- Fork : créer `kazo/v0.18.1` sur `7cc73ab1`
  (`git fetch https://github.com/storytold/deckcraft refs/tags/v0.18.1`), la pousser, sous-module
  `external/deckcraft` dessus.
- `tools/build-deckcraft.sh` (Rust stable du jour, cible `wasm32-unknown-unknown`, Trunk,
  `CRAFT_FONTS_DIR` sur un clone de `craft-fonts` à `37827ad`, `trunk build --release` dans
  `apps/deckcraft-web`), `tools/vendor-deckcraft.sh`, workflow `build-deckcraft.yml`, test xUnit
  « le commit de `deckcraft.json` est celui du sous-module ». Modèles : `Os-App-Fig`, `Os-App-PDF`.
- Page d'iframe (décision 5) et fenêtre sur `CraftFrame` et `FrameStartup` : chargement, app,
  erreur, « Réessayer » ; thème à `ready` (décision 10).
- **Terminé quand** : l'app démarre en WebGPU et en WebGL 2, deux fenêtres tournent ensemble, le
  `.kazoapp` s'installe dans le Kazo OS 3.3.4 publié ; taille du `.wasm` comparée au zip officiel,
  taille du paquet et mémoire par fenêtre notées ici.
- **Fait le 2026-11-14** (PR vers `deck/base`). Fork : branche `kazo/v0.18.1` de
  `Kazo-ca/deckcraft` sur `7cc73ab1`, sans autre changement ; sous-module `external/deckcraft`
  dessus, sous-module `external/KazoOs` sur `v3.3.4`. Compilé par `tools/build-deckcraft.sh`
  (rustc 1.97.0, Trunk 0.21.14, `craft-fonts` `37827ad`, `CRAFT_FONTS_REQUIRED`), le
  `packaging/web/package.sh` de DeckCraft tel quel. Vérifié :
  - `dotnet build -c Release` et `dotnet test -c Release` : 39 tests verts (dont « le commit de
    `deckcraft.json` est celui du sous-module » et les tailles et SHA-256 de chaque fichier).
  - `npm run test:e2e` (hôte `Kazo.Deck.Os` en Release, Chromium sans carte graphique) : 3 parcours
    verts : la page de l'iframe sans paramètre `control` ; DeckCraft en WebGL 2 (`--webgl`), fenêtre
    affichée à `ready`, aucun écran restant, thème sombre de l'OS reçu par `ui.set { dark }`,
    une présentation vide ; deux fenêtres ensemble, chacune son instance (une diapositive ajoutée
    dans l'une ne change pas l'autre).
  - `npm run test:installed` (`tests/installed/install-and-start.mjs`, neuf) : le `.kazoapp`
    s'installe par `sudo kazopkg install` dans le **Kazo OS 3.3.4 publié**
    (`kazoos-wasm-3.3.4.zip`), DeckCraft s'affiche en 4,9 s à la première fenêtre (téléchargement
    et compilation, serveur local), 1,3 s à la deuxième ; sans drapeau, DeckCraft s'est rabattu
    seul sur WebGL 2 (pas de WebGPU dans ce Chromium).
  - **Non vérifié** : le rendu **WebGPU** (le WebGPU logiciel de Chromium perd son appareil sans
    carte graphique, comme pour Kazo Fig et Kazo Photo) ; à essayer sur une machine avec GPU.
  - Les Debug (`KazoOsUseSource=true`) ne compilent pas dans ce conteneur (SDK .NET 10.0.115,
    erreurs Razor dans `Kazo.Os.Apps.Browser` du sous-module) ; la CI et le Store utilisent le
    profil Release, vert.

  | Mesure | Valeur |
  | --- | --- |
  | `.wasm` compilé | **38 745 366 octets** (36,9 Mio ; gzip -9 : 15 254 992) |
  | `.wasm` du zip officiel | 33 159 566 octets (31,6 Mio ; gzip -9 : 11 840 503) |
  | Écart | **+5 585 800 octets (+16,8 %)** : les polices de `craft-fonts`. Inter normal et gras (832 068 octets) et BIZ UDPGothic (4 669 688 octets) font 5,50 Mo ; l'écart a la taille de ces polices et le zip officiel n'a aucune licence OFL : la release amont semble compilée sans `CRAFT_FONTS_DIR` (déduit, pas vérifié dans ses journaux). Le nôtre a les polices que la release promet. |
  | Paquet `ca.kazo.deck-0.1.0.kazoapp` | 15 373 139 octets (14,7 Mio) ; 37,3 Mio décompressé, 43 fichiers |
  | Mémoire par fenêtre | processus de rendu de Chromium (`ps`, rendu logiciel) : +347 Mio à la première fenêtre, +46 Mio à la deuxième ; la mémoire du processus GPU n'est pas comptée. Mesure grossière, à refaire sur une machine avec GPU. |

  Notes pour la suite : `package.sh` copie la licence OFL de **toutes** les familles de
  `craft-fonts` (30 fichiers `OFL-*.txt`), alors que le build web n'en intègre que deux ; elles
  restent dans `wwwroot/deckcraft/` (rien de faux, quelques Ko de trop), le chantier 5 en tient
  compte dans `THIRD-PARTY-NOTICES`. `Ctrl+N` (nouvelle fenêtre, Q1 = A) n'est pas encore réservé :
  il vient avec la fenêtre du chantier 3.

### 3. Fenêtre (`deck/3-window`)

- Ouverture : fichier demandé au lancement ; Fichier › Ouvrir de DeckCraft montre le dialogue de
  l'OS (`PickRequested` avec les extensions de présentation), DeckCraft entend « annulé » et la
  fenêtre envoie elle-même `deckcraft:open` avec le nom, pour que le fichier soit lié ; fenêtre
  ou nouvelle fenêtre selon Q1. Les autres sélecteurs (image, son, vidéo) reçoivent les fichiers lus.
- Enregistrement et copies : décisions 6 et 7, de bout en bout, y compris Enregistrer sous et
  Exporter par les pages de DeckCraft ; un dialogue annulé ou une écriture refusée laisse la
  présentation modifiée, avec un message. Exports à plusieurs fichiers (une image par
  diapositive) : relever le comportement et choisir la forme la moins pénible (un dialogue par
  fichier, ou un dossier choisi une fois) ; poser la question à Benoit si les deux se valent.
- Liaison et titre (décision 8), garde de fermeture (décision 9), raccourcis (décision 12),
  thème suivi en direct.
- Mesures : un `.pptx` de quelques dizaines de Mo (temps d'ouverture et d'enregistrement,
  mémoire) ; lecture d'un son et d'une vidéo insérés.
- **Terminé quand** : aucun cas ne perd de modifications sans que l'utilisateur l'ait choisi,
  aucun dialogue ni téléchargement du navigateur ne sort de l'app, un export n'écrase jamais un
  fichier sans le demander, et les raccourcis de l'OS marchent le curseur dans la diapositive.

### 4. Diaporama (`deck/4-slide-show`)

- Selon Q2 : script de la page (`wwwroot/host/`, fichier propre à l'app, sans script en ligne)
  qui suit `ui.inspect.showing`, demande le plein écran dans le délai du geste de l'utilisateur,
  termine le diaporama à la sortie du plein écran, et se rabat sur « remplit la fenêtre » en
  cas de refus. Vérifier `F5`, `Maj+F5`, le bouton « Present », `?show` et la fin par la
  dernière diapositive.
- Selon Q3 : `show.presenter { on: false }` à `ready` et `window.open` neutralisé dans la page ;
  vérifier que DeckCraft ne laisse pas de bouton « Open presenter window » à l'écran.
- **Terminé quand** : un diaporama lancé d'un clic passe en plein écran et en sort proprement,
  et rien ne s'ouvre en dehors de Kazo OS (ou selon les réponses à Q2 et Q3).
- **Fait le 2026-11-14** (PR vers `deck/base`). Q2 = A et Q3 = A, sans changer Kazo OS ni
  DeckCraft :
  - `wwwroot/host/slide-show.js` (module chargé par la page de l'iframe, commenté) : après chaque
    touche ou clic **vrais** de la page (pas les évènements rejoués), trois questions
    `uiInspect().showing` à 60, 200 et 450 ms ; un diaporama qui vient de démarrer passe le canevas
    en plein écran (`requestFullscreen`), une fois par diaporama, tant que le geste le permet. La
    sortie du plein écran (Échap, ou le navigateur) envoie `show.end` ; un diaporama qui finit seul
    (après la dernière diapositive) quitte le plein écran. Refusé : le diaporama remplit la
    fenêtre. Hors diaporama, **aucune minuterie** : seulement ces trois questions après un geste
    (chacune coûte une image de DeckCraft) ; pendant le diaporama, une question toutes les 500 ms
    pour voir sa fin. L'état est écrit sur `<html data-deck-show>` (`full-screen`, `window`).
  - Mode présentateur : la fenêtre envoie `engine.execute { command: "show.presenter",
    params: { on: false } }` à `ready` (`DeckCraftShow`; `show.presenter` seul n'est pas une
    méthode de contrôle) ; le script le recoupe s'il le voit allumé (bouton « Presenter View » de
    l'onglet Diaporama) et refuse `window.open` **pour l'adresse du présentateur seulement**
    (`#deckcraft-presenter=`), les liens des diapositives s'ouvrent toujours ; `deck-page.css`
    cache le bouton « Open presenter window » que DeckCraft pose sur `body` si un diaporama démarre
    quand même avec le présentateur allumé. Les notes restent visibles en mode Normal.
  - Vérifié : `dotnet build -c Release` et `dotnet test -c Release` (42 tests verts, dont la page
    qui charge le script et la feuille, la commande du présentateur) ; `npm run test:unit`
    (10 tests du script sur une page simulée : plein écran, Échap, fin seule, refus, une seule
    rafale par geste, évènements rejoués ignorés, aucune minuterie hors diaporama) ;
    `npm run test:e2e` (hôte en Release, Chromium sans carte graphique, WebGL 2) : `F5` passe en
    plein écran sans recharger la page, Échap termine le diaporama et le focus reste dans la fenêtre
    Kazo (`F5` relance) ; `Maj+F5` part de la diapositive courante ; le bouton « Present » ;
    la fin après la dernière diapositive quitte le plein écran ; présentateur rallumé : aucune
    fenêtre du navigateur, aucun bouton visible, recoupé ; un diaporama lancé sans geste remplit la
    fenêtre puis passe en plein écran au clic suivant.
  - **Non vérifié** : `?show` tel quel (la fenêtre ne le met jamais ; le cas « diaporama lancé sans
    geste » le couvre) ; Échap réel du navigateur en plein écran (Chromium sans affichage garde la
    touche : le parcours sort du plein écran par `exitFullscreen`, ce que fait le navigateur) ;
    Firefox et Safari (délai du geste plus court sous Safari) ; le rendu WebGPU.

### 5. Store (`deck/5-store`)

- Icône sans logo ArtCraft, fiche fr et en (« basé sur DeckCraft de l'équipe ArtCraft » ; limites
  connues : alpha, interface en anglais, 128 Mio par fichier, polices de remplacement, mode
  présentateur, fenêtre qui reste sur son écran), captures, README refait sur la fiche
  (`store:readme`, « Essayer » avec `?autolaunch=ca.kazo.deck`), `THIRD-PARTY-NOTICES` (DeckCraft,
  ses avis, les polices intégrées).
- Parcours dans le Kazo OS publié (`tests/installed/`), workflow « Tag a release », PR au
  catalogue `Os-AppMarketPlace` (la signature se fait là, aucune clé ici), ligne de `features.md`
  dans `Kazo-ca/Os` (fr et en).
- **Terminé quand** : Kazo Deck s'installe depuis le Store, ouvre un `.pptx` par double-clic,
  l'enregistre et le présente.

## Piste en amont

Brouillons dans `/mnt/project-files/deck/upstream/deckcraft-web-host.md`, publiés par Benoit :

- le build web ignore la demande de plein écran du diaporama, et l'hôte n'apprend pas qu'un
  diaporama démarre ou se termine ;
- `ui.inspect.documents` sans identifiant ni document actif ;
- le mode présentateur en mode hébergé (fenêtre ouverte par `window.open`, sans moyen pour
  l'hôte de la placer) ;
- se joindre à `craft-libs` #611 (confirmation d'enregistrement `saved`).

Non vérifié : les issues déjà ouvertes en amont sur ces sujets (`storytold` est hors de portée de
la session) ; Benoit regarde avant de publier.

## Avancement

- [x] Étape 0 : lecture de DeckCraft 0.18.1 et essai du build web officiel en mode hébergé.
- [x] Plan fr et en.
- [x] Réponses de Benoit à Q1, Q2 et Q3 : A partout (2026-11-14).
- [x] Chantier 2 : solution, manifeste, compilation depuis le fork, page d'iframe, fenêtre (2026-11-14).
- [ ] Chantier 3 : fenêtre, ouverture, enregistrement, copies, garde de fermeture.
- [x] Chantier 4 : diaporama en plein écran, mode présentateur coupé (2026-11-14).
- [ ] Chantier 5 : Store.
- [ ] Sortie : `deck/base` dans `main` (Benoit), étiquette `v0.1.0`, catalogue, `features.md`.
