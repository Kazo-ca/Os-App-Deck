// Kazo Deck as the Kazo OS Store installs it (work items 2 and 5 of doc/en/plan.md, "Done when"):
// `npm run test:installed -- [--store] <Kazo OS wwwroot> [packages] [screenshots folder]`.
//
// --store: installs from the real Kazo OS Store (the published catalog of Os-AppMarketPlace, read
//   over the network) instead of `kazopkg install` from [packages]: the check after a release.
//
// <Kazo OS wwwroot>: a published Kazo OS, for example the kazoos-wasm-<version>.zip of a Kazo OS
//   release, unzipped (the trimmed OS users run; 3.3.4 is the minimum).
// [packages]: the folder `dotnet build src/Kazo.Deck -c Release -t:KazoOsPack` writes (default
//   src/Kazo.Deck/bin/kazoos-packages), the content of the kazoos-packages branch.
// [screenshots folder]: when given, the run also writes the Store's screenshots there (1.png …).
//
// The run serves the OS as a plain static host, answers the GitHub addresses of this repository's
// packages from [packages], installs Kazo Deck with `sudo kazopkg install` (its API check against
// the trimmed runtime included), starts it from the launcher (no launch argument: DeckCraft picks
// its renderer itself) and a second window next to it, and prints how long each took to show
// DeckCraft and how much the page's memory grew. In the first window it starts a presentation from
// DeckCraft's "Pitch" starter template and exports it as pitch.pptx through the OS "Save a copy"
// dialog. Then a double-click on pitch.pptx in the File Explorer opens it in a new window, a slide
// is added and saved in place, F5 shows it in full screen, and the licenses are checked. Not part
// of CI: it needs WebGL 2 or WebGPU in the browser and several minutes.
import { createServer } from 'node:http';
import { mkdir, readFile, stat } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { chromium } from 'playwright';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const fromStore = process.argv.includes('--store');
const [wwwrootArgument, packagesArgument, shotsArgument] = process.argv.slice(2).filter(argument => argument !== '--store');
if (!wwwrootArgument) {
    console.error('usage: npm run test:installed -- [--store] <Kazo OS wwwroot> [packages folder] [screenshots folder]');
    process.exit(2);
}
const wwwroot = path.resolve(wwwrootArgument);
const packages = path.resolve(packagesArgument ?? path.join(root, 'src', 'Kazo.Deck', 'bin', 'kazoos-packages'));
const shots = shotsArgument && path.resolve(shotsArgument);

const REPOSITORY = 'Kazo-ca/Os-App-Deck';
const APP = 'Kazo Deck';
const LOGIN = 'neo';
const PASSWORD = 'matrix123';
const HOME = `/home/${LOGIN}`;
const STEP_MS = 120_000;
// DeckCraft's first start downloads about 37 MB of .wasm and compiles it.
const START_MS = 240_000;
const TYPES = {
    '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css',
    '.json': 'application/json', '.wasm': 'application/wasm', '.svg': 'image/svg+xml', '.png': 'image/png',
    '.webmanifest': 'application/manifest+json', '.dat': 'application/octet-stream', '.blat': 'application/octet-stream',
};
// No WebGPU flag: Chromium on Linux has no WebGPU by default, so DeckCraft falls back to WebGL 2
// by itself, which ANGLE draws on the CPU (SwiftShader). Chromium's software WebGPU adapter loses
// its device in headless runs (tests/e2e/deck.e2e.mjs), so it is not used here.
const browserOptions = { args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] };

const log = message => console.log(`[installed] ${message}`);

// A plain static server, as a release host is: no COOP/COEP, no cache.
const server = createServer(async (request, response) => {
    const relative = decodeURIComponent(new URL(request.url, 'http://host').pathname);
    let file = path.join(wwwroot, relative);
    if (!file.startsWith(wwwroot)) {
        response.writeHead(403).end();
        return;
    }
    try {
        if ((await stat(file)).isDirectory()) file = path.join(file, 'index.html');
        const body = await readFile(file);
        response.writeHead(200, { 'Content-Type': TYPES[path.extname(file)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
        response.end(body);
    } catch {
        response.writeHead(404).end();
    }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const baseUrl = `http://127.0.0.1:${server.address().port}/`;

const waitForText = async (locator, predicate, what) => {
    const deadline = Date.now() + STEP_MS;
    while (Date.now() < deadline) {
        const text = (await locator.innerText()).replace(/ /g, ' ');
        if (predicate(text)) return text;
        await new Promise(resolve => setTimeout(resolve, 250));
    }
    throw new Error(`timed out waiting for ${what}`);
};

const deckWindows = page => page.locator('article[data-window-id]', { has: page.locator('.deck-window') });
const windowById = (page, id) => page.locator(`article[data-window-id="${id}"]`);

/** Waits for a new Kazo Deck window showing DeckCraft; returns its id and how long it took. */
async function waitForNewWindow(page, known, timeout = START_MS) {
    const started = Date.now();
    const deadline = started + timeout;
    while (Date.now() < deadline) {
        const ids = await deckWindows(page).evaluateAll(nodes => nodes
            .filter(node => node.querySelector('.deck-frame.running'))
            .map(node => node.getAttribute('data-window-id')));
        const id = ids.find(candidate => !known.includes(candidate));
        if (id) return { id, ms: Date.now() - started };
        await new Promise(resolve => setTimeout(resolve, 100));
    }
    const failure = await deckWindows(page).locator('.deck-screen.failed').allInnerTexts().catch(() => []);
    throw new Error(`no new Kazo Deck window showed DeckCraft ${failure.join(' | ')}`);
}

async function frameOf(page, id) {
    const handle = await windowById(page, id).locator('iframe.craft-frame-iframe').elementHandle();
    return handle.contentFrame();
}

async function shoot(page, name) {
    if (!shots) return;
    await mkdir(shots, { recursive: true });
    // Let DeckCraft paint its last frame.
    await page.waitForTimeout(1500);
    await page.screenshot({ path: path.join(shots, name) });
    log(`screenshot ${name}`);
}

/**
 * Resident memory of Chromium's renderer processes, in MiB (Linux `ps`; 0 elsewhere). The OS page
 * and its same-origin iframes share one renderer, and DeckCraft's WebAssembly memory is not in the
 * JavaScript heap, so this is what shows the cost of a window.
 */
const rendererMiB = async () => {
    try {
        const { execFileSync } = await import('node:child_process');
        const lines = execFileSync('ps', ['-eo', 'rss=,args='], { encoding: 'utf8' }).split('\n');
        const kib = lines.filter(line => line.includes('--type=renderer')).reduce((sum, line) => sum + Number.parseInt(line, 10), 0);
        return kib / 1024;
    } catch {
        return 0;
    }
};

async function openApp(page, name) {
    await page.getByRole('button', { name: 'App launcher' }).click({ timeout: STEP_MS });
    await page.getByRole('combobox', { name: 'Search applications' }).fill(name);
    await page.getByRole('button', { name: `Launch ${name}`, exact: true }).click();
}

/** `sudo kazopkg install` in the Terminal, as an administrator. */
async function installWithKazopkg(page) {
    await openApp(page, 'Terminal');
    const rows = page.locator('[data-terminal-panel] .xterm-rows').first();
    await waitForText(rows, text => text.includes('$'), 'the shell prompt');
    await page.locator('[data-terminal-panel] .xterm-screen').first().click();
    await page.keyboard.type(`sudo kazopkg install ${REPOSITORY}`);
    await page.keyboard.press('Enter');
    const prompt = page.locator('.credential-prompt-dialog, .aaa-window').first();
    await prompt.waitFor({ state: 'visible', timeout: STEP_MS });
    const login = prompt.locator('input[type=text]');
    if (await login.count() > 0 && !(await login.inputValue())) await login.fill(LOGIN);
    await prompt.locator('input[type=password]').fill(PASSWORD);
    await prompt.locator('input[type=password]').press('Enter');
    const output = await waitForText(rows, text => text.includes('available now') || /does not have|refused|error/i.test(text), 'the end of the installation');
    if (!output.includes('available now')) throw new Error(`kazopkg did not install ${APP}:\n${output}`);
    log(`kazopkg installed ${APP}.`);
    await page.locator('[data-terminal-panel]').first().locator('xpath=ancestor::article[1]').locator('header.window-chrome .chrome-button-close').click();
}

/** The Kazo OS Store: search Kazo Deck in the published catalog, "Install for me". */
async function installFromStore(page) {
    await openApp(page, 'Kazo OS Store');
    const store = page.locator('.store-root').first();
    await store.locator('input.store-search').fill(APP);
    // Kazo OS 3.3.4 Store: a card (not a button) whose title opens the app's page.
    const card = store.locator('.store-card', { hasText: APP }).first();
    await card.waitFor({ timeout: STEP_MS });
    await card.getByText(APP, { exact: true }).first().click();
    await store.getByRole('button', { name: 'Install for me', exact: true }).click({ timeout: STEP_MS });
    // A first install from the catalog may ask an administrator.
    const prompt = page.locator('.credential-prompt-dialog, .aaa-window').first();
    const opened = store.getByRole('button', { name: 'Open', exact: true });
    await Promise.race([
        prompt.waitFor({ state: 'visible', timeout: STEP_MS }).then(async () => {
            const login = prompt.locator('input[type=text]');
            if (await login.count() > 0 && !(await login.inputValue())) await login.fill(LOGIN);
            await prompt.locator('input[type=password]').fill(PASSWORD);
            await prompt.locator('input[type=password]').press('Enter');
        }),
        opened.waitFor({ timeout: STEP_MS }),
    ]).catch(() => {});
    try {
        await opened.waitFor({ timeout: STEP_MS * 2 });
    } catch (error) {
        const banner = await store.locator('.store-banner').allInnerTexts().catch(() => []);
        throw new Error(`the Store did not install ${APP}: ${banner.join(' | ')}`, { cause: error });
    }
    log(`The Kazo OS Store installed ${APP}.`);
    await store.locator('xpath=ancestor::article[1]').locator('header.window-chrome .chrome-button-close').click();
}

/** A DeckCraft command (window.deckcraft in the iframe), as its ribbon and menus run it. */
const execute = (frame, command, params = {}) => frame.evaluate(([name, args]) => window.deckcraft.execute(name, args), [command, params]);

/** Waits until DeckCraft's presentations (ui.inspect documents) match `check`. */
async function waitForDocuments(frame, check, what) {
    const deadline = Date.now() + STEP_MS;
    let last = [];
    while (Date.now() < deadline) {
        last = await frame.evaluate(async () => (await window.deckcraft.uiInspect())?.documents ?? []).catch(error => [String(error)]);
        if (check(last)) return last;
        await new Promise(resolve => setTimeout(resolve, 250));
    }
    throw new Error(`DeckCraft's presentations are not ${what}: ${JSON.stringify(last)}`);
}

async function expectTitle(page, id, text) {
    try {
        await page.waitForFunction(
            ([windowId, wanted]) => document.querySelector(`article[data-window-id="${windowId}"] header.window-chrome h2`)?.textContent?.trim() === wanted,
            [id, text],
            { timeout: STEP_MS });
    } catch (error) {
        const actual = await windowById(page, id).locator('header.window-chrome h2').first().textContent().catch(() => null);
        throw new Error(`title "${actual}" instead of "${text}"`, { cause: error });
    }
}

/** Answers the OS save dialog titled `title` with a file name. */
async function answerSaveDialog(page, title, fileName) {
    const dialog = page.locator('section.file-dialog', { hasText: title });
    await dialog.waitFor({ state: 'visible', timeout: STEP_MS });
    await dialog.getByLabel('File name').fill(fileName);
    await dialog.getByRole('button', { name: 'Save' }).click();
    await dialog.waitFor({ state: 'hidden', timeout: STEP_MS });
}

/** Closes a Kazo Deck window; "No" when it asks to save (the template's presentation). */
async function closeWithoutSaving(page, id) {
    await windowById(page, id).locator('header.window-chrome .chrome-button-close').click();
    const question = page.locator('.message-box-dialog', { hasText: 'Save the changes' });
    await Promise.race([
        question.waitFor({ timeout: 10_000 }).then(() => question.getByRole('button', { name: 'No' }).click()),
        windowById(page, id).waitFor({ state: 'detached', timeout: 10_000 }),
    ]).catch(() => {});
    await windowById(page, id).waitFor({ state: 'detached', timeout: STEP_MS });
}

/** The page's slide-show state: full screen element, <html data-deck-show>, and whether a show runs. */
const showState = frame => frame.evaluate(async () => ({
    fullScreen: document.fullscreenElement?.id ?? null,
    mode: document.documentElement.dataset.deckShow ?? null,
    showing: (await window.deckcraft.uiInspect()).showing ?? null,
}));

async function waitForShow(frame, check, what) {
    const deadline = Date.now() + 30_000;
    let state;
    while (Date.now() < deadline) {
        state = await showState(frame);
        if (check(state)) return state;
        await new Promise(resolve => setTimeout(resolve, 150));
    }
    throw new Error(`the slide show is not ${what}: ${JSON.stringify(state)}`);
}

/** egui paints on input: move the pointer over the canvas so the last frame is drawn. */
async function repaint(page, id) {
    const box = await windowById(page, id).locator('iframe.craft-frame-iframe').boundingBox();
    for (let step = 0; step < 6; step++) {
        await page.mouse.move(box.x + box.width / 2 + step * 7, box.y + box.height - 60 - step * 5);
        await page.waitForTimeout(300);
    }
}

// The Store reads the catalog on GitHub: go through the machine's proxy when there is one.
const proxy = process.env.HTTPS_PROXY ?? process.env.https_proxy;
if (fromStore && proxy) browserOptions.proxy = { server: proxy, bypass: '127.0.0.1,localhost' };

const browser = await chromium.launch(browserOptions);
const errors = [];
let page;
try {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, ignoreHTTPSErrors: fromStore && Boolean(proxy) });
    // raw.githubusercontent.com, as kazopkg reads it (ADR 0182 of Kazo OS), answered locally.
    if (!fromStore) await context.route(new RegExp(`^https://raw\\.githubusercontent\\.com/${REPOSITORY}/kazoos-packages/`), async route => {
        const relative = decodeURIComponent(new URL(route.request().url()).pathname).split('/kazoos-packages/')[1];
        const file = path.join(packages, relative);
        const headers = { 'Access-Control-Allow-Origin': '*' };
        try {
            if (!file.startsWith(packages)) throw new Error('outside the packages folder');
            await route.fulfill({ status: 200, body: await readFile(file), headers: { ...headers, 'Content-Type': 'application/octet-stream' } });
            log(`served ${relative}`);
        } catch {
            await route.fulfill({ status: 404, headers });
        }
    });
    page = await context.newPage();
    page.on('pageerror', error => errors.push(String(error)));

    // First boot: the administrator is created.
    await page.goto(baseUrl, { waitUntil: 'domcontentloaded' });
    await page.getByRole('heading', { name: 'Create administrator' }).waitFor({ timeout: 180_000 });
    await page.getByLabel('Login name').fill(LOGIN);
    await page.getByLabel('Display name').fill('Neo');
    await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
    await page.getByLabel('Confirm password').fill(PASSWORD);
    await page.getByRole('button', { name: 'Create local profile' }).click();
    await page.getByText('Applications', { exact: true }).waitFor({ timeout: STEP_MS });

    if (fromStore) {
        await installFromStore(page);
    } else {
        await installWithKazopkg(page);
    }
    const baseline = await rendererMiB();

    // Kazo Deck from the launcher: the first start downloads and compiles DeckCraft.
    await openApp(page, APP);
    const first = await waitForNewWindow(page, []);
    const frame = await frameOf(page, first.id);
    const renderer = await frame.evaluate(() => {
        const canvas = document.getElementById('deckcraft_canvas');
        return canvas.getContext('webgpu') ? 'WebGPU' : canvas.getContext('webgl2') ? 'WebGL 2' : 'none';
    });
    const ui = await frame.evaluate(() => window.deckcraft.uiInspect());
    if (ui?.ui?.brightness !== 'dark') throw new Error(`DeckCraft did not take the OS theme: ${JSON.stringify(ui?.ui)}`);
    const afterFirst = await rendererMiB();
    log(`First window: DeckCraft shown after ${(first.ms / 1000).toFixed(1)} s (download and compile), renderer ${renderer}, renderer memory +${(afterFirst - baseline).toFixed(0)} MiB.`);

    // A second window: its own DeckCraft, from the browser cache.
    await openApp(page, APP);
    const second = await waitForNewWindow(page, [first.id]);
    const afterSecond = await rendererMiB();
    log(`Second window: DeckCraft shown after ${(second.ms / 1000).toFixed(1)} s, renderer memory +${(afterSecond - afterFirst).toFixed(0)} MiB.`);
    await closeWithoutSaving(page, second.id);

    // DeckCraft's "Pitch" starter template, exported as pitch.pptx: Export › PowerPoint is a copy
    // (decision 6), written through the OS "Save a copy" dialog.
    await execute(frame, 'file.new', { template: 'Pitch' });
    await waitForDocuments(frame, docs => docs.some(doc => doc.title !== 'Presentation1' && doc.title), 'on the Pitch template');
    await frame.evaluate(() => window.deckcraft.request('ui.dialog.open', { id: 'export', fields: { fmt: 'pptx' } }));
    await frame.locator('#deckcraft_canvas').focus();
    await page.keyboard.press('Enter');
    await answerSaveDialog(page, 'Save a copy', 'pitch.pptx');
    await windowById(page, first.id).locator('.deck-notice', { hasText: 'Copy saved: pitch.pptx' }).waitFor({ timeout: STEP_MS });
    log('The Pitch template was exported as pitch.pptx through the OS dialog.');
    await closeWithoutSaving(page, first.id);

    // A double-click on pitch.pptx in the File Explorer: the installed app's file type opens it.
    await openApp(page, 'File Explorer');
    const row = name => page.locator(`tr[data-item-path="${HOME}/${name}"]`).first();
    await row('pitch.pptx').waitFor({ timeout: STEP_MS });
    await row('pitch.pptx').dblclick({ position: { x: 40, y: 10 } });
    const opened = await waitForNewWindow(page, []);
    log(`Window on pitch.pptx: DeckCraft shown after ${(opened.ms / 1000).toFixed(1)} s (DeckCraft in the browser cache).`);
    await expectTitle(page, opened.id, `pitch.pptx — ${APP}`);
    const pitch = await frameOf(page, opened.id);
    await waitForDocuments(pitch, docs => docs.length === 1 && docs[0].path === 'pitch.pptx', 'on pitch.pptx');
    const slides = (await pitch.evaluate(() => window.deckcraft.docInspect())).slides.length;
    log(`A double-click on pitch.pptx opened it in ${APP} (${slides} slides).`);

    // A slide added and saved in place: the window is bound to pitch.pptx, no dialog.
    await execute(pitch, 'slide.new', {});
    await expectTitle(page, opened.id, `*pitch.pptx — ${APP}`);
    await execute(pitch, 'file.save');
    await expectTitle(page, opened.id, `pitch.pptx — ${APP}`);
    if (await page.locator('section.file-dialog').count() > 0) throw new Error('saving in place showed a dialog');
    await execute(pitch, 'slide.go', { index: 0 });
    log(`${APP} saved pitch.pptx in place (${slides + 1} slides).`);

    // The licenses came with the package.
    for (const file of ['legal/LICENSE.txt', 'legal/THIRD-PARTY-NOTICES.md', 'legal/THIRD-PARTY-NOTICES-fr.md', 'legal/deckcraft/ATTRIBUTION.md',
        'legal/deckcraft/LICENSE-symphonia-MPL-2.0.txt', 'deckcraft/NOTICE', 'deckcraft/LICENSE-MIT', 'deckcraft/LICENSE-APACHE', 'deckcraft/OFL-inter.txt']) {
        const status = await pitch.evaluate(async url => (await fetch(url)).status, new URL(`../${file}`, pitch.url()).href);
        if (status !== 200) throw new Error(`${file} is not in the installed package (${status})`);
    }
    log('The licenses are installed with the app.');

    // Screenshot 3: a File Explorer opened in front of the window, pitch.pptx selected.
    for (const close of await page.locator('article[data-window-id]', { hasText: 'File Explorer' }).locator('header.window-chrome .chrome-button-close').all()) {
        await close.dispatchEvent('click');
    }
    await openApp(page, 'File Explorer');
    await row('pitch.pptx').waitFor({ timeout: STEP_MS });
    await row('pitch.pptx').click({ position: { x: 40, y: 10 } });
    await shoot(page, '3.png');
    for (const close of await page.locator('article[data-window-id]', { hasText: 'File Explorer' }).locator('header.window-chrome .chrome-button-close').all()) {
        await close.dispatchEvent('click');
    }

    // Screenshot 1: the presentation, the window maximized.
    const chrome = windowById(page, opened.id).locator('header.window-chrome').first();
    await chrome.dblclick({ position: { x: 300, y: 12 } });
    await page.waitForTimeout(2000);
    await repaint(page, opened.id);
    await shoot(page, '1.png');

    // F5: the slide show in full screen (Q2 = A), then out of it.
    const box = await windowById(page, opened.id).locator('iframe.craft-frame-iframe').boundingBox();
    await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
    await page.keyboard.press('F5');
    await waitForShow(pitch, state => state.showing !== null && state.fullScreen === 'deckcraft_canvas' && state.mode === 'full-screen', 'in full screen');
    log('F5 showed pitch.pptx in full screen.');
    await shoot(page, '2.png');
    await page.keyboard.press('Escape');
    if ((await showState(pitch)).fullScreen !== null) await pitch.evaluate(() => document.exitFullscreen());
    await waitForShow(pitch, state => state.showing === null && state.fullScreen === null, 'over');
    log('The slide show ended and the window is back.');

    if (errors.length > 0) throw new Error(`page errors: ${errors.join(' | ')}`);
    log('PASS');
} catch (error) {
    console.error(`[installed] FAIL: ${error.message}`);
    await page?.screenshot({ path: 'installed-failure.png' }).catch(() => {});
    process.exitCode = 1;
} finally {
    await browser.close();
    server.close();
}
