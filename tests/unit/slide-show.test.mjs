// Unit tests of the iframe page's slide-show script (src/Kazo.Deck/wwwroot/host/slide-show.js,
// doc/en/plan.md, work item 4), on a fake page: `npm run test:unit`. The real browser and DeckCraft
// are covered by tests/e2e/slide-show.e2e.mjs.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
    CHECKS_AFTER_GESTURE_MS, followSlideShow, isPresenterUrl, refusePresenterPopup,
} from '../../src/Kazo.Deck/wwwroot/host/slide-show.js';

const settle = () => new Promise(resolve => setTimeout(resolve, 0));

/** A page with DeckCraft's API, manual timers, and a document that can go full screen. */
function fakePage({ allowFullScreen = true } = {}) {
    const listeners = new Map();
    const timers = new Map();
    let nextTimer = 1;
    const executed = [];
    const deck = { showing: null, presenterView: false };

    const document = new EventTarget();
    document.documentElement = { dataset: {} };
    document.fullscreenElement = null;
    document.exitFullscreen = async () => {
        document.fullscreenElement = null;
        document.dispatchEvent(new Event('fullscreenchange'));
    };

    const canvas = {
        requestFullscreen: async () => {
            if (!allowFullScreen) {
                throw new Error('Permissions check failed');
            }
            document.fullscreenElement = canvas;
            document.dispatchEvent(new Event('fullscreenchange'));
        },
    };

    const page = {
        navigator: { userActivation: { isActive: true } },
        deckcraft: {
            uiInspect: async () => ({ showing: deck.showing, ui: { presenterView: deck.presenterView } }),
            execute: async (command, params) => {
                executed.push([command, params]);
                if (command === 'show.end') {
                    deck.showing = null;
                }
                if (command === 'show.presenter') {
                    deck.presenterView = params.on;
                }
                return {};
            },
        },
        addEventListener: (type, listener) => listeners.set(type, listener),
        removeEventListener: type => listeners.delete(type),
        setTimeout: (callback, delay) => {
            timers.set(nextTimer, { callback, delay, repeat: false });
            return nextTimer++;
        },
        setInterval: (callback, delay) => {
            timers.set(nextTimer, { callback, delay, repeat: true });
            return nextTimer++;
        },
        clearTimeout: id => timers.delete(id),
        clearInterval: id => timers.delete(id),
    };

    /** Runs every pending timer once, letting the promises settle after each. */
    const runTimers = async () => {
        for (const [id, timer] of [...timers]) {
            if (!timer.repeat) {
                timers.delete(id);
            }
            timer.callback();
            await settle();
        }
    };

    /** Sends one event to the page's listener, without running the timers. */
    const dispatch = (type, isTrusted = true) => listeners.get(type)?.({ isTrusted });

    /** A trusted gesture, then its checks. */
    const gesture = async (type = 'keydown') => {
        dispatch(type);
        await runTimers();
    };

    return { page, document, canvas, deck, executed, timers, listeners, dispatch, gesture, runTimers };
}

test('the presenter popup is refused, other addresses still open', () => {
    const opened = [];
    const page = { open: (url) => { opened.push(url); return { url }; } };
    refusePresenterPopup(page);

    assert.equal(page.open('https://example.test/index.html?host=parent#deckcraft-presenter=1-2', '_blank', 'popup'), null);
    assert.deepEqual(page.open('https://example.test/link', '_blank'), { url: 'https://example.test/link' });
    assert.deepEqual(opened, ['https://example.test/link']);
    assert.equal(isPresenterUrl(undefined), false);
});

test('a gesture outside a show leaves no timer once its checks are done', async () => {
    const fake = fakePage();
    followSlideShow(fake);

    await fake.gesture();

    assert.equal(fake.timers.size, 0, 'no polling outside a show');
    assert.equal(fake.document.fullscreenElement, null);
    assert.equal(fake.document.documentElement.dataset.deckShow, undefined);
});

test('gestures during a burst of checks do not start another burst', () => {
    const fake = fakePage();
    followSlideShow(fake);

    fake.dispatch('keydown');
    fake.dispatch('keydown');
    fake.dispatch('pointerup');

    assert.equal(fake.timers.size, CHECKS_AFTER_GESTURE_MS.length);
});

test('synthetic events are not gestures', () => {
    const fake = fakePage();
    followSlideShow(fake);

    // The module's proxy replays keys as untrusted events: they cannot start a show's full screen.
    fake.dispatch('keydown', false);

    assert.equal(fake.timers.size, 0);
});

test('a show started by a gesture goes to full screen, and Esc ends it', async () => {
    const fake = fakePage();
    followSlideShow(fake);

    fake.deck.showing = { slide: 0 };
    await fake.gesture();
    assert.equal(fake.document.fullscreenElement, fake.canvas);
    assert.equal(fake.document.documentElement.dataset.deckShow, 'full-screen');
    assert.ok(fake.timers.size > 0, 'the show is polled while it runs');

    // Esc: the browser leaves full screen, the script ends the show.
    fake.document.fullscreenElement = null;
    fake.document.dispatchEvent(new Event('fullscreenchange'));
    await settle();

    assert.deepEqual(fake.executed.filter(([command]) => command === 'show.end'), [['show.end', {}]]);
    assert.equal(fake.deck.showing, null);
    assert.equal(fake.document.documentElement.dataset.deckShow, undefined);
    assert.equal(fake.timers.size, 0, 'polling stops with the show');
});

test('a show that ends by itself leaves full screen without ending again', async () => {
    const fake = fakePage();
    followSlideShow(fake);
    fake.deck.showing = { slide: 0 };
    await fake.gesture();
    assert.equal(fake.document.fullscreenElement, fake.canvas);

    // After the last slide: DeckCraft ends the show, the poll sees it.
    fake.deck.showing = null;
    await fake.runTimers();

    assert.equal(fake.document.fullscreenElement, null);
    assert.deepEqual(fake.executed.filter(([command]) => command === 'show.end'), []);
    assert.equal(fake.timers.size, 0);
});

test('a refused full screen leaves the show in the window, asked once per show', async () => {
    const fake = fakePage({ allowFullScreen: false });
    let asked = 0;
    const request = fake.canvas.requestFullscreen;
    fake.canvas.requestFullscreen = () => { asked++; return request(); };
    followSlideShow(fake);

    fake.deck.showing = { slide: 0 };
    await fake.gesture();
    await fake.gesture('pointerup');

    assert.equal(asked, 1);
    assert.equal(fake.document.fullscreenElement, null);
    assert.equal(fake.document.documentElement.dataset.deckShow, 'window');
    assert.deepEqual(fake.executed, [], 'the show goes on');
});

test('a show started without a gesture goes to full screen on the next one', async () => {
    const fake = fakePage();
    const follower = followSlideShow(fake);
    fake.page.navigator.userActivation.isActive = false;
    fake.deck.showing = { slide: 0 };

    await follower.check();
    assert.equal(fake.document.fullscreenElement, null);

    fake.page.navigator.userActivation.isActive = true;
    await fake.gesture('pointerup');
    assert.equal(fake.document.fullscreenElement, fake.canvas);
});

test('the presenter view turned on again goes back off', async () => {
    const fake = fakePage();
    followSlideShow(fake);
    fake.deck.presenterView = true;

    await fake.gesture('pointerup');

    assert.equal(fake.deck.presenterView, false);
    assert.deepEqual(fake.executed[0], ['show.presenter', { on: false }]);
});

test('stop removes the listeners and the timers', async () => {
    const fake = fakePage();
    const follower = followSlideShow(fake);
    fake.deck.showing = { slide: 0 };
    await fake.gesture();
    assert.ok(fake.timers.size > 0);

    follower.stop();
    assert.equal(fake.timers.size, 0);
    assert.equal(fake.listeners.size, 0);
});
