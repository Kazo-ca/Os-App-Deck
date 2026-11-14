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
