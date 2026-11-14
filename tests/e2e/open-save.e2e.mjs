// End-to-end runs of opening, saving, copies and closing (doc/en/plan.md, work item 3,
// decisions 6 to 9, Q1 = A: one presentation per window), inside the Kazo OS host:
// `npm run test:e2e` after `dotnet build Kazo.Deck.slnx`.
//
// DeckCraft is driven through its page API (window.deckcraft in the iframe) to change a
// presentation and run its commands, as its ribbon and menus do; File › Save As, Print and
// Export run DeckCraft's own interface commands (app.saveAsDialog, app.print, the Export dialog),
// since file.saveAs and file.export by path do not work in a browser. Everything else goes through
// the OS: its save and open dialogs, its message box, the window's title bar and close button, and
// the Terminal (`xdg-open` starts the app associated with the file type). DeckCraft starts with
// --webgl: Chromium's software WebGPU adapter loses its device in headless runs (deck.e2e.mjs).
import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { runInTerminal, startHost } from './host.mjs';
import {
    HOME, STEP_TIMEOUT_MS, answerSaveDialog, bootPage, deckWindows, documentsOf, execute, expectTitle, frameOf, launchBrowser, modify,
    shot, waitForDocuments, waitForWindow, windowById,
} from './common.mjs';

const SAVE_TITLE = 'Save the presentation';
const COPY_TITLE = 'Save a copy';
const OPEN_TITLE = 'Open a presentation';

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

/** Opens Kazo Deck on WebGL 2 from the Terminal; returns its window id and DeckCraft's frame. */
async function launch(page, known = [], argument = '') {
    await runInTerminal(page, `launch ca.kazo.deck --webgl ${argument}`.trim());
    const id = await waitForWindow(page, known.length + 1, known);
    return { id, frame: await frameOf(page, id) };
}

/** Waits for the Terminal's output to show `text`. */
async function expectInTerminal(page, text) {
    await page.locator('.xterm-rows').last().getByText(text, { exact: false }).first().waitFor({ timeout: STEP_TIMEOUT_MS });
}

/** The Terminal's listing of a folder. */
async function listing(page, folder) {
    await runInTerminal(page, `ls ${folder}`);
    await page.waitForTimeout(1_000);
    return page.locator('.xterm-rows').last().innerText();
}

/** The window's notice, once it says `text`. */
const notice = (page, id, text) =>
    windowById(page, id).locator('.deck-notice', { hasText: text }).waitFor({ timeout: STEP_TIMEOUT_MS });

/**
 * Lets an OS file dialog finish watching the folder it just entered before it closes (a Kazo OS
 * file view bug, seen with Kazo Photo); a person never picks a file that fast.
 */
const settleAfterNavigation = page => page.waitForTimeout(1_000);

/** A new presentation saved as `fileName` in the home folder, through File › Save and the OS dialog. */
async function saveNewPresentation(page, id, frame, fileName) {
    await modify(frame);
    await execute(frame, 'file.save');
    await answerSaveDialog(page, SAVE_TITLE, fileName);
    await expectTitle(page, id, `${fileName} — Kazo Deck`);
}

/** DeckCraft's File › Open: the OS dialog, and a double-click on `fileName`. */
async function pickInOpenDialog(page, frame, fileName) {
    await execute(frame, 'app.openDialog');
    const dialog = page.locator('section.file-dialog[role="dialog"]').last();
    await dialog.getByRole('heading', { name: OPEN_TITLE }).waitFor({ timeout: STEP_TIMEOUT_MS });
    await settleAfterNavigation(page);
    await dialog.getByText(fileName, { exact: true }).dblclick().catch(async error => {
        await shot(page, 'open-dialog');
        throw error;
    });
    await dialog.waitFor({ state: 'detached', timeout: STEP_TIMEOUT_MS });
}

test('a new presentation is saved where the user says, saved again in place, and reopens from the OS', { timeout: 400_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    const { id, frame } = await launch(page);
    await expectTitle(page, id, 'Presentation1 — Kazo Deck');

    await modify(frame);
    await expectTitle(page, id, '*Presentation1 — Kazo Deck');

    // Decision 7: the first save is a .deckcraft file, and asks where to write it.
    await execute(frame, 'file.save');
    const dialog = page.locator('section.file-dialog', { hasText: SAVE_TITLE });
    await dialog.waitFor({ timeout: STEP_TIMEOUT_MS });
    assert.equal(await dialog.getByLabel('File name').inputValue(), 'Presentation1.deckcraft');
    await answerSaveDialog(page, SAVE_TITLE, 'talk.deckcraft');
    await expectTitle(page, id, 'talk.deckcraft — Kazo Deck');

    // The next save writes the same file, with no dialog.
    await modify(frame);
    await expectTitle(page, id, '*talk.deckcraft — Kazo Deck');
    await execute(frame, 'file.save');
    await expectTitle(page, id, 'talk.deckcraft — Kazo Deck');
    assert.equal(await page.locator('section.file-dialog').count(), 0);

    // `xdg-open` starts the app associated with .deckcraft: a second window, on the file, with
    // the three slides saved.
    await runInTerminal(page, `xdg-open ${HOME}/talk.deckcraft`);
    const second = await waitForWindow(page, 2, [id]);
    await expectTitle(page, second, 'talk.deckcraft — Kazo Deck');
    const reopened = await frameOf(page, second);
    const list = await waitForDocuments(reopened, docs => docs.some(doc => doc.path === 'talk.deckcraft'), 'on talk.deckcraft');
    assert.equal(list.length, 1, 'the file took the place of the blank presentation');
    const doc = await reopened.evaluate(() => window.deckcraft.docInspect());
    assert.equal(doc.slides.length, 3);

    assert.deepEqual(errors, []);
    await page.close();
});

test('Save As binds the presentation to a new file; Print and Export write copies, the binding stays', { timeout: 500_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    const { id, frame } = await launch(page);
    await saveNewPresentation(page, id, frame, 'first.deckcraft');

    // File › Save As (Ctrl+Shift+S) is DeckCraft's own: saveAs true, a new file.
    await execute(frame, 'app.saveAsDialog');
    await answerSaveDialog(page, SAVE_TITLE, 'second.deckcraft');
    await expectTitle(page, id, 'second.deckcraft — Kazo Deck');
    await modify(frame);
    await execute(frame, 'file.save');
    await expectTitle(page, id, 'second.deckcraft — Kazo Deck');
    assert.equal(await page.locator('section.file-dialog').count(), 0);

    // Print hands a PDF over with saveAs false: a copy, asked for (decision 6).
    await modify(frame);
    await execute(frame, 'app.print');
    await answerSaveDialog(page, COPY_TITLE, 'handout.pdf');
    await notice(page, id, 'Copy saved: handout.pdf');
    await expectTitle(page, id, '*second.deckcraft — Kazo Deck');

    // Export as pictures of every slide: one save per slide. The first one asks, the others
    // follow it into its folder under DeckCraft's names.
    await runInTerminal(page, `mkdir ${HOME}/Slides`);
    await frame.evaluate(() => window.deckcraft.request('ui.dialog.open', { id: 'export', fields: { fmt: 'pngAll' } }));
    await frame.locator('#deckcraft_canvas').focus();
    await page.keyboard.press('Enter');
    const copy = page.locator('section.file-dialog', { hasText: COPY_TITLE });
    await copy.waitFor({ timeout: STEP_TIMEOUT_MS });
    const suggested = await copy.getByLabel('File name').inputValue();
    await copy.getByText('Slides', { exact: true }).dblclick();
    await copy.getByText('This folder is empty.').waitFor({ timeout: STEP_TIMEOUT_MS });
    await settleAfterNavigation(page);
    await copy.getByLabel('File name').fill(suggested);
    await copy.getByRole('button', { name: 'Save' }).click();
    await copy.waitFor({ state: 'hidden', timeout: STEP_TIMEOUT_MS });
    const slides = (await frame.evaluate(() => window.deckcraft.docInspect())).slides.length;
    const stem = suggested.replace(/1\.png$/, '');
    await notice(page, id, `Copy saved: ${stem}${slides}.png`);
    assert.equal(await page.locator('section.file-dialog').count(), 0, 'one dialog for the whole export');

    const pictures = await listing(page, `${HOME}/Slides`);
    for (let n = 1; n <= slides; n++) {
        assert.match(pictures, new RegExp(`${stem}${n}\\.png`));
    }
    const home = await listing(page, HOME);
    assert.match(home, /first\.deckcraft/);
    assert.match(home, /second\.deckcraft/);
    assert.match(home, /handout\.pdf/);
    await expectTitle(page, id, '*second.deckcraft — Kazo Deck');

    assert.deepEqual(errors, []);
    await page.close();
});

test('closing with changes asks; Cancel keeps the window, Yes saves before closing', { timeout: 400_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    const { id, frame } = await launch(page);
    await modify(frame);
    await expectTitle(page, id, '*Presentation1 — Kazo Deck');

    const close = windowById(page, id).locator('header.window-chrome .chrome-button-close');
    const question = page.locator('.message-box-dialog', { hasText: 'Save the changes' });

    // Cancel: nothing closes, nothing is lost.
    await close.click();
    await question.waitFor({ timeout: STEP_TIMEOUT_MS });
    await question.getByRole('button', { name: 'Cancel' }).click();
    await question.waitFor({ state: 'hidden', timeout: STEP_TIMEOUT_MS });
    assert.equal(await windowById(page, id).count(), 1);

    // Yes, then Cancel in the save dialog: the window stays open, and stays modified although
    // DeckCraft counts the presentation saved once it handed the bytes over (§6 of the guide).
    await close.click();
    await question.waitFor({ timeout: STEP_TIMEOUT_MS });
    await question.getByRole('button', { name: 'Yes' }).click();
    const saveDialog = page.locator('section.file-dialog', { hasText: SAVE_TITLE });
    await saveDialog.waitFor({ timeout: STEP_TIMEOUT_MS });
    await saveDialog.getByRole('button', { name: 'Cancel' }).click();
    await notice(page, id, 'was not saved');
    assert.equal(await windowById(page, id).count(), 1);
    // DeckCraft now names the presentation after the file it handed over; the window still says
    // it is not in a file.
    await expectTitle(page, id, '*Presentation1.deckcraft — Kazo Deck');

    // Yes, then a file name: the presentation is written, then the window closes.
    await close.click();
    await question.waitFor({ timeout: STEP_TIMEOUT_MS });
    await question.getByRole('button', { name: 'Yes' }).click();
    await answerSaveDialog(page, SAVE_TITLE, 'closing.deckcraft');
    await windowById(page, id).waitFor({ state: 'detached', timeout: STEP_TIMEOUT_MS });

    await runInTerminal(page, `xdg-open ${HOME}/closing.deckcraft`);
    const reopened = await waitForWindow(page, 1);
    await expectTitle(page, reopened, 'closing.deckcraft — Kazo Deck');

    assert.deepEqual(errors, []);
    await page.close();
});

test('File › Open uses the OS dialog: in the blank window, then in a new window (one presentation per window)', { timeout: 500_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    const first = await launch(page);
    await saveNewPresentation(page, first.id, first.frame, 'alpha.deckcraft');
    await execute(first.frame, 'app.saveAsDialog');
    await answerSaveDialog(page, SAVE_TITLE, 'beta.deckcraft');
    await expectTitle(page, first.id, 'beta.deckcraft — Kazo Deck');
    await windowById(page, first.id).locator('header.window-chrome .chrome-button-close').click();
    await windowById(page, first.id).waitFor({ state: 'detached', timeout: STEP_TIMEOUT_MS });

    // A fresh window still holds its untouched blank presentation: the file takes its place.
    const fresh = await launch(page);
    await pickInOpenDialog(page, fresh.frame, 'alpha.deckcraft');
    await expectTitle(page, fresh.id, 'alpha.deckcraft — Kazo Deck');
    assert.deepEqual((await documentsOf(fresh.frame)).map(doc => doc.path), ['alpha.deckcraft']);

    // That window has a presentation now: the next file opens in its own window (Q1 = A).
    await pickInOpenDialog(page, fresh.frame, 'beta.deckcraft');
    const second = await waitForWindow(page, 2, [fresh.id]);
    await expectTitle(page, second, 'beta.deckcraft — Kazo Deck');
    assert.deepEqual((await documentsOf(fresh.frame)).map(doc => doc.path), ['alpha.deckcraft']);
    await expectTitle(page, fresh.id, 'alpha.deckcraft — Kazo Deck');

    // Picking the file the window already shows opens nothing more.
    await pickInOpenDialog(page, fresh.frame, 'alpha.deckcraft');
    await page.waitForTimeout(2_000);
    assert.equal(await deckWindows(page).count(), 2);

    assert.deepEqual(errors, []);
    await page.close();
});

test('Ctrl+N in a presentation opens a new Kazo Deck window, not a hidden presentation', { timeout: 400_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    const { id, frame } = await launch(page);

    await frame.locator('#deckcraft_canvas').focus();
    await page.keyboard.press('Control+KeyN');
    const second = await waitForWindow(page, 2, [id]);
    await expectTitle(page, second, 'Presentation1 — Kazo Deck');
    assert.equal((await documentsOf(frame)).length, 1, 'DeckCraft did not get Ctrl+N');

    assert.deepEqual(errors, []);
    await page.close();
});

test('a .pptx opened from the OS saves back to itself as .pptx; an OS chord still reaches the OS', { timeout: 500_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    const { id, frame } = await launch(page);
    await modify(frame);

    // Export › PowerPoint: a copy named <title>.pptx (saveAs false, a name bound to nothing).
    await frame.evaluate(() => window.deckcraft.request('ui.dialog.open', { id: 'export', fields: { fmt: 'pptx' } }));
    await frame.locator('#deckcraft_canvas').focus();
    await page.keyboard.press('Enter');
    await answerSaveDialog(page, COPY_TITLE, 'deck.pptx');
    await notice(page, id, 'Copy saved: deck.pptx');
    await expectTitle(page, id, '*Presentation1 — Kazo Deck');

    // Double-click in the Files app is xdg-open: a new window, bound to the .pptx.
    await runInTerminal(page, `xdg-open ${HOME}/deck.pptx`);
    const second = await waitForWindow(page, 2, [id]);
    await expectTitle(page, second, 'deck.pptx — Kazo Deck');
    const pptx = await frameOf(page, second);
    await waitForDocuments(pptx, docs => docs.length === 1 && docs[0].path === 'deck.pptx', 'on deck.pptx');

    // Save writes the .pptx in place, with no dialog.
    await modify(pptx);
    await expectTitle(page, second, '*deck.pptx — Kazo Deck');
    await execute(pptx, 'file.save');
    await expectTitle(page, second, 'deck.pptx — Kazo Deck');
    assert.equal(await page.locator('section.file-dialog').count(), 0);

    // Alt+Q, an OS chord, typed in DeckCraft: the OS closes the window, through the close guard.
    await modify(pptx);
    await expectTitle(page, second, '*deck.pptx — Kazo Deck');
    await pptx.locator('#deckcraft_canvas').focus();
    await page.keyboard.press('Alt+KeyQ');
    const question = page.locator('.message-box-dialog', { hasText: 'Save the changes' });
    await question.waitFor({ timeout: STEP_TIMEOUT_MS });
    await question.getByRole('button', { name: 'Yes' }).click();
    await windowById(page, second).waitFor({ state: 'detached', timeout: STEP_TIMEOUT_MS });
    assert.equal(await page.locator('section.file-dialog').count(), 0, 'a bound .pptx saves with no dialog');

    await runInTerminal(page, `xdg-open ${HOME}/deck.pptx`);
    const third = await waitForWindow(page, 2, [id]);
    const again = await frameOf(page, third);
    await waitForDocuments(again, docs => docs[0]?.path === 'deck.pptx', 'on deck.pptx again');
    const doc = await again.evaluate(() => window.deckcraft.docInspect());
    assert.equal(doc.slides.length, 4, 'the two slides added in the .pptx window were saved');

    assert.deepEqual(errors, []);
    await page.close();
});
