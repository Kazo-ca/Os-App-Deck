// Shared helpers of the end-to-end runs: the browser, the Kazo Deck windows and what runs in
// their iframes (DeckCraft's page).
import { chromium } from 'playwright';
import { boot } from './host.mjs';

// DeckCraft's first start downloads about 32 MB of .wasm from the local host and compiles it.
export const START_TIMEOUT_MS = 120_000;

// A software WebGPU adapter (SwiftShader), so both of DeckCraft's renderers can be tried.
export const launchBrowser = () => chromium.launch({
    args: ['--enable-unsafe-webgpu', '--enable-features=Vulkan', '--use-vulkan=swiftshader', '--use-webgpu-adapter=swiftshader', '--use-angle=vulkan'],
});

/** A fresh page with the OS booted; `errors` collects the page's uncaught errors. */
export async function bootPage(browser, baseUrl) {
    const page = await browser.newPage({ viewport: { width: 1400, height: 900 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await boot(page, baseUrl);
    return { page, errors };
}

/** Kazo Deck's OS windows, in opening order. */
export const deckWindows = page => page.locator('article[data-window-id]', { has: page.locator('.deck-window') });

/** The iframe element of the window at `index` (opening order). */
export const iframeAt = (page, index) => deckWindows(page).nth(index).locator('iframe.craft-frame-iframe');

/** The DeckCraft frame of the window at `index`, once the module put the iframe there. */
export async function frameAt(page, index) {
    const iframe = iframeAt(page, index);
    await iframe.waitFor({ state: 'attached', timeout: START_TIMEOUT_MS });
    return (await iframe.elementHandle()).contentFrame();
}

/**
 * Waits until DeckCraft runs in a frame: its start code removes #deckcraft_loading once eframe
 * started on the canvas (apps/deckcraft-web/src/web.rs), and the canvas then has a size.
 */
export async function waitForDeckCraft(frame) {
    await frame.waitForFunction(() => {
        const canvas = document.getElementById('deckcraft_canvas');
        return document.getElementById('deckcraft_loading') === null && canvas !== null && canvas.width > 0;
    }, undefined, { timeout: START_TIMEOUT_MS });
}

/** The renderer DeckCraft's canvas uses: `webgpu`, `webgl2` or null. */
export const rendererOf = frame => frame.evaluate(() => {
    const canvas = document.getElementById('deckcraft_canvas');
    if (canvas.getContext('webgpu')) {
        return 'webgpu';
    }
    return canvas.getContext('webgl2') ? 'webgl2' : null;
});

// The steps of a run that waits on the OS or on DeckCraft, once DeckCraft runs.
export const STEP_TIMEOUT_MS = 30_000;
export const HOME = '/home/neo';

/** Saves a screenshot of the page when DECK_E2E_SHOTS names a folder. */
export async function shot(page, name) {
    if (process.env.DECK_E2E_SHOTS) {
        await page.screenshot({ path: `${process.env.DECK_E2E_SHOTS}/${name}.png` });
    }
}

/** One OS window by its id. */
export const windowById = (page, id) => page.locator(`article[data-window-id="${id}"]`);

/** Waits for `count` Kazo Deck windows to show DeckCraft; returns the id of the newest one. */
export async function waitForWindow(page, count, known = []) {
    try {
        await page.locator('.deck-frame.running').nth(count - 1).waitFor({ timeout: START_TIMEOUT_MS });
    } catch (error) {
        await shot(page, `window-${count}`);
        throw error;
    }
    const ids = await deckWindows(page).evaluateAll(nodes => nodes.map(node => node.getAttribute('data-window-id')));
    if (ids.length !== count) {
        throw new Error(`${ids.length} Kazo Deck windows instead of ${count}`);
    }
    return ids.find(id => !known.includes(id));
}

/** The DeckCraft frame of a window, by the window's id. */
export async function frameOf(page, id) {
    const handle = await windowById(page, id).locator('iframe.craft-frame-iframe').elementHandle();
    return handle.contentFrame();
}

/** Runs a DeckCraft command (engine or interface), as its ribbon and menus do. */
export const execute = (frame, command, params = {}) =>
    frame.evaluate(([name, args]) => window.deckcraft.execute(name, args), [command, params]);

/** DeckCraft's open presentations (ui.inspect documents: { title, dirty, path }). */
export const documentsOf = async frame => (await frame.evaluate(() => window.deckcraft.uiInspect()))?.documents ?? [];

/** Waits until DeckCraft's presentations match `check` (a function of the list). */
export async function waitForDocuments(frame, check, description) {
    const deadline = Date.now() + STEP_TIMEOUT_MS;
    let last = [];
    while (Date.now() < deadline) {
        last = await documentsOf(frame).catch(error => [String(error)]);
        if (check(last)) {
            return last;
        }
        await new Promise(resolve => setTimeout(resolve, 200));
    }
    throw new Error(`DeckCraft's presentations are not ${description}: ${JSON.stringify(last)}`);
}

/** Adds a slide, so that the presentation in front has unsaved changes. */
export async function modify(frame) {
    await execute(frame, 'slide.new', {});
    await waitForDocuments(frame, list => list.some(doc => doc.dirty), 'modified after slide.new');
}

/** Answers the OS save dialog with a file name (in the folder it opens in). */
export async function answerSaveDialog(page, title, fileName) {
    const dialog = page.locator('section.file-dialog', { hasText: title });
    await dialog.waitFor({ state: 'visible', timeout: STEP_TIMEOUT_MS });
    await dialog.getByLabel('File name').fill(fileName);
    await dialog.getByRole('button', { name: 'Save' }).click();
    await dialog.waitFor({ state: 'hidden', timeout: STEP_TIMEOUT_MS });
}

/** Waits for a window's title bar to read `text`; on failure, says what it reads and the window's notice. */
export async function expectTitle(page, id, text) {
    try {
        await page.waitForFunction(
            ([windowId, wanted]) => document.querySelector(`article[data-window-id="${windowId}"] header.window-chrome h2`)?.textContent?.trim() === wanted,
            [id, text],
            { timeout: STEP_TIMEOUT_MS });
    } catch (error) {
        const actual = await windowById(page, id).locator('header.window-chrome h2').first().textContent().catch(() => null);
        const notice = await windowById(page, id).locator('.deck-notice').textContent().catch(() => null);
        await shot(page, `title-${id}`);
        throw new Error(`title "${actual}" instead of "${text}"; notice: ${notice}`, { cause: error });
    }
}
