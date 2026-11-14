// End-to-end runs of Kazo Deck's slide show (doc/en/plan.md, work item 4; Q2 = A browser full
// screen, Q3 = A presenter view off): `npm run test:e2e` after the build. DeckCraft runs on WebGL 2
// (`--webgl`), see deck.e2e.mjs. Each test opens a fresh page.
import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { runInTerminal, startHost } from './host.mjs';
import { bootPage, frameAt, iframeAt, launchBrowser, START_TIMEOUT_MS, waitForDeckCraft } from './common.mjs';

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

/** DeckCraft's interface state (`window.deckcraft.uiInspect`). */
const inspect = frame => frame.evaluate(() => window.deckcraft.uiInspect());

/** The page's slide-show state: full screen element, <html data-deck-show>, and whether a show runs. */
const showState = frame => frame.evaluate(async () => ({
    fullScreen: document.fullscreenElement?.id ?? null,
    mode: document.documentElement.dataset.deckShow ?? null,
    showing: (await window.deckcraft.uiInspect()).showing ?? null,
}));

/** Waits until `check(showState())` holds. */
async function waitForShow(frame, check, timeoutMs = 15_000) {
    const deadline = Date.now() + timeoutMs;
    let state;
    while (Date.now() < deadline) {
        state = await showState(frame);
        if (check(state)) {
            return state;
        }
        await new Promise(resolve => setTimeout(resolve, 150));
    }
    assert.fail(`the slide show never matched: ${JSON.stringify(state)}`);
}

const inFullScreen = state => state.showing !== null && state.fullScreen === 'deckcraft_canvas' && state.mode === 'full-screen';
const stopped = state => state.showing === null && state.fullScreen === null && state.mode === null;

/**
 * Opens a Kazo Deck window ready for a show: DeckCraft running, `slides` slides, the presenter
 * view turned off by the window, and the pointer clicked once on the slide (the keyboard goes to
 * DeckCraft). Popups the browser opens are collected in `popups`.
 */
async function openDeck({ slides = 1 } = {}) {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    const popups = [];
    page.context().on('page', popup => popups.push(popup.url()));

    await runInTerminal(page, 'launch ca.kazo.deck --webgl');
    const frame = await frameAt(page, 0);
    await waitForDeckCraft(frame);
    await page.locator('.deck-frame.running').first().waitFor({ timeout: START_TIMEOUT_MS });
    for (let added = 1; added < slides; added++) {
        await frame.evaluate(() => window.deckcraft.execute('slide.new', {}));
    }

    // Q3 = A: the window turned DeckCraft's presenter view off once it was ready.
    const deadline = Date.now() + 15_000;
    while ((await inspect(frame)).ui.presenterView !== false) {
        assert.ok(Date.now() < deadline, 'the presenter view stays on');
        await new Promise(resolve => setTimeout(resolve, 200));
    }

    const box = await iframeAt(page, 0).boundingBox();
    await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
    return { page, frame, box, errors, popups };
}

/** Leaves full screen as the browser does on Esc when headless Chromium keeps the key. */
async function pressEscape(page, frame) {
    await page.keyboard.press('Escape');
    const state = await showState(frame);
    if (state.fullScreen !== null) {
        await frame.evaluate(() => document.exitFullscreen());
    }
}

test('F5 shows the slides in full screen, Esc ends the show and the keyboard stays in the window', { timeout: 300_000 }, async () => {
    const { page, frame, errors, popups } = await openDeck();

    await page.keyboard.press('F5');
    await waitForShow(frame, inFullScreen);
    assert.equal(new URL(frame.url()).pathname.endsWith('/host/index.html'), true, 'F5 did not reload the page');

    await pressEscape(page, frame);
    await waitForShow(frame, stopped);

    // The focus is back in the Kazo window: its iframe has it, and F5 starts another show.
    assert.equal(await page.evaluate(() => document.activeElement?.classList.contains('craft-frame-iframe')), true);
    await page.keyboard.press('F5');
    await waitForShow(frame, inFullScreen);
    await pressEscape(page, frame);
    await waitForShow(frame, stopped);

    assert.deepEqual(popups, []);
    assert.deepEqual(errors, []);
    await page.close();
});

test('Shift+F5 shows from the current slide in full screen', { timeout: 300_000 }, async () => {
    const { page, frame, errors } = await openDeck({ slides: 3 });
    // slide.new selected the last slide added.
    const current = (await frame.evaluate(() => window.deckcraft.docInspect())).selection?.slide ?? 2;

    await page.keyboard.press('Shift+F5');
    const state = await waitForShow(frame, inFullScreen);
    assert.equal(state.showing.slide, current);

    await pressEscape(page, frame);
    await waitForShow(frame, stopped);
    assert.deepEqual(errors, []);
    await page.close();
});

test('the Present button shows the slides in full screen', { timeout: 300_000 }, async () => {
    const { page, frame, box, errors } = await openDeck();

    // "Present", at the right end of DeckCraft's title bar.
    await page.mouse.click(box.x + box.width - 83, box.y + 19);
    await waitForShow(frame, inFullScreen);

    await pressEscape(page, frame);
    await waitForShow(frame, stopped);
    assert.deepEqual(errors, []);
    await page.close();
});

test('a show that ends after the last slide leaves full screen', { timeout: 300_000 }, async () => {
    const { page, frame, errors } = await openDeck({ slides: 2 });

    await page.keyboard.press('F5');
    await waitForShow(frame, inFullScreen);

    // Past the last slide DeckCraft shows its end screen, and one more key ends the show.
    for (let step = 0; step < 6 && (await showState(frame)).showing !== null; step++) {
        await page.keyboard.press('ArrowRight');
        await page.waitForTimeout(400);
    }
    await waitForShow(frame, stopped);

    assert.deepEqual(errors, []);
    await page.close();
});

test('the presenter view never opens a browser window, even turned on again', { timeout: 300_000 }, async () => {
    const { page, frame, errors, popups } = await openDeck();

    // Turned on without a gesture (as the Slide Show tab would, just before F5).
    await frame.evaluate(() => window.deckcraft.execute('show.presenter', { on: true }));
    await page.keyboard.press('F5');
    await waitForShow(frame, inFullScreen);

    assert.deepEqual(popups, [], 'no presenter popup');
    // DeckCraft's "Open presenter window" button is not shown over the slides.
    const visibleButtons = await frame.evaluate(() => [...document.querySelectorAll('body > button')]
        .filter(button => button.getClientRects().length > 0).length);
    assert.equal(visibleButtons, 0);
    // The script turned the view back off for the next show.
    assert.equal((await inspect(frame)).ui.presenterView, false);

    await pressEscape(page, frame);
    await waitForShow(frame, stopped);
    assert.deepEqual(errors, []);
    await page.close();
});

test('a show started without a gesture fills the window, then goes to full screen on a click', { timeout: 300_000 }, async () => {
    const { page, frame, box, errors } = await openDeck({ slides: 3 });
    // Past the checks that followed openDeck's click: nothing asks DeckCraft until the next gesture.
    await page.waitForTimeout(1000);
    await frame.evaluate(() => window.deckcraft.execute('show.fromStart', {}));
    await page.waitForTimeout(1500);

    let state = await showState(frame);
    assert.notEqual(state.showing, null);
    assert.equal(state.fullScreen, null, 'no full screen without a gesture');

    await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
    state = await waitForShow(frame, inFullScreen);

    await pressEscape(page, frame);
    await waitForShow(frame, stopped);
    assert.deepEqual(errors, []);
    await page.close();
});
