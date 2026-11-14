// End-to-end runs of the Kazo Deck window inside the Kazo OS host (doc/en/plan.md, work item 2):
// `npm run test:e2e` after `dotnet build Kazo.Deck.slnx -c Debug`. Each test opens a fresh page.
//
// Chromium without a graphics card has no usable WebGPU: SwiftShader's software adapter loses its
// device right after the start. So the runs that wait for DeckCraft to draw start it with the
// `--webgl` launch argument (`webgl` in the page's query, WebGL 2 on SwiftShader); WebGPU is checked
// on a machine with a GPU (doc/en/plan.md, "Progress").
import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { openApp, runInTerminal, startHost } from './host.mjs';
import { bootPage, deckWindows, frameAt, launchBrowser, rendererOf, START_TIMEOUT_MS, waitForDeckCraft } from './common.mjs';

let host;
let browser;

before(async () => {
    host = await startHost();
    browser = await launchBrowser();
});

after(async () => {
    await browser?.close();
    host?.stop();
});

/** What DeckCraft's command API says about its interface (`window.deckcraft.uiInspect`, a promise). */
const inspect = frame => frame.evaluate(() => window.deckcraft.uiInspect());

/** Number of slides of the active presentation (`window.deckcraft.docInspect`). */
const slideCount = frame => frame.evaluate(async () => {
    const doc = await window.deckcraft.docInspect();
    return Array.isArray(doc.slides) ? doc.slides.length : doc.slides;
});

/** Waits until `check(uiInspect())` holds. */
async function waitForUi(frame, check, timeoutMs = 30_000) {
    const deadline = Date.now() + timeoutMs;
    let ui;
    while (Date.now() < deadline) {
        ui = await inspect(frame);
        if (check(ui)) {
            return ui;
        }
        await new Promise(resolve => setTimeout(resolve, 250));
    }
    assert.fail(`DeckCraft's interface never matched: ${JSON.stringify(ui)?.slice(0, 400)}`);
}

test('Kazo Deck opens from the Applications menu with no parameter in the page', { timeout: 300_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);

    await openApp(page, 'Kazo Deck');
    const frame = await frameAt(page, 0);
    const url = new URL(frame.url());
    // Decision 5: DeckCraft installs window.deckcraft in every mode, no "control" parameter.
    assert.equal(url.pathname.endsWith('/_content/Kazo.Deck/host/index.html'), true, url.pathname);
    assert.equal(url.searchParams.has('control'), false);
    assert.equal(url.searchParams.has('webgl'), false);

    assert.deepEqual(errors, []);
    await page.close();
});

test('the window shows DeckCraft on WebGL 2 once it is ready, in the OS theme', { timeout: 300_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);

    await runInTerminal(page, 'launch ca.kazo.deck --webgl');
    const frame = await frameAt(page, 0);
    assert.equal(new URL(frame.url()).searchParams.has('webgl'), true);
    await waitForDeckCraft(frame);
    assert.equal(await rendererOf(frame), 'webgl2');

    // deckcraft:ready reached the window: the iframe is shown and no screen is left over it.
    await page.locator('.deck-frame.running').first().waitFor({ timeout: START_TIMEOUT_MS });
    assert.equal(await page.locator('.deck-screen').count(), 0, 'no loading or error screen is left');

    // DeckCraft starts on an empty presentation; the window gave it the OS theme (dark by
    // default) with ui.set { dark } (decision 10).
    const ui = await waitForUi(frame, state => state?.ui?.brightness === 'dark');
    assert.equal(ui.documents.length, 1);
    assert.equal(ui.documents[0].dirty, false);

    assert.deepEqual(errors, []);
    await page.close();
});

test('two Kazo Deck windows run their own DeckCraft at the same time', { timeout: 400_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);

    await runInTerminal(page, 'launch ca.kazo.deck --webgl');
    const first = await frameAt(page, 0);
    await waitForDeckCraft(first);
    await runInTerminal(page, 'launch ca.kazo.deck --webgl');
    const second = await frameAt(page, 1);
    await waitForDeckCraft(second);

    assert.equal(await deckWindows(page).count(), 2);
    assert.equal(await page.locator('.deck-frame.running').count(), 2);
    assert.notEqual(first, second);

    // Each instance holds its own presentation: a slide added in the first one stays there.
    const before = await slideCount(second);
    await first.evaluate(() => window.deckcraft.execute('slide.new', {}));
    assert.equal(await slideCount(first), before + 1);
    assert.equal(await slideCount(second), before);

    assert.deepEqual(errors, []);
    await page.close();
});
