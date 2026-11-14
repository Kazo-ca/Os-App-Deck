// Kazo Deck as Kazo OS installs it (work item 2 of doc/en/plan.md, "Done when"):
// `npm run test:installed -- <Kazo OS wwwroot> [packages] [screenshots folder]`.
//
// <Kazo OS wwwroot>: a published Kazo OS, for example the kazoos-wasm-<version>.zip of a Kazo OS
//   release, unzipped (the trimmed OS users run; 3.3.4 is the minimum).
// [packages]: the folder `dotnet build src/Kazo.Deck -c Release -t:KazoOsPack` writes (default
//   src/Kazo.Deck/bin/kazoos-packages), the content of the kazoos-packages branch.
// [screenshots folder]: when given, the run also writes a screenshot of the two windows there.
//
// The run serves the OS as a plain static host, answers the GitHub addresses of this repository's
// packages from [packages], installs Kazo Deck with `sudo kazopkg install` (its API check against
// the trimmed runtime included), starts it from the launcher (no launch argument: DeckCraft picks
// its renderer itself), then a second window next to it, and prints how long each took to show
// DeckCraft and how much the page's memory grew. Work item 5 extends it (open, save, Store). Not
// part of CI: it needs WebGL 2 or WebGPU in the browser and several minutes.
import { createServer } from 'node:http';
import { mkdir, readFile, stat } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { chromium } from 'playwright';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const [wwwrootArgument, packagesArgument, shotsArgument] = process.argv.slice(2);
if (!wwwrootArgument) {
    console.error('usage: npm run test:installed -- <Kazo OS wwwroot> [packages folder] [screenshots folder]');
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

const browser = await chromium.launch(browserOptions);
const errors = [];
let page;
try {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    // raw.githubusercontent.com, as kazopkg reads it (ADR 0182 of Kazo OS), answered locally.
    await context.route(new RegExp(`^https://raw\\.githubusercontent\\.com/${REPOSITORY}/kazoos-packages/`), async route => {
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

    await installWithKazopkg(page);
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
    await shoot(page, 'two-windows.png');

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
