// The Kazo Deck slide show in the browser's full screen (doc/en/plan.md, work item 4; Benoit's
// answers Q2 = A and Q3 = A, 2026-11-14).
//
// DeckCraft asks eframe for full screen when a slide show starts, and its web build ignores the
// request: on its own the show fills the iframe, so the Kazo window. This script, loaded by the
// iframe page next to the module's proxy, does the rest from the page:
//
// - Start: after each key press or click in the page (the gestures that can start a show: F5,
//   Shift+F5, "Present", the Slide Show tab), it asks DeckCraft whether a show runs
//   (`window.deckcraft.uiInspect().showing`). A show that just started gets the canvas in the
//   browser's full screen, while the gesture still allows it. If the browser refuses, the show keeps
//   filling the window.
// - End: Esc (the browser leaves full screen first) or any other way out of full screen ends the
//   show (`show.end`). A show that ends by itself (after the last slide, from DeckCraft's menu)
//   leaves full screen. While a show runs the script asks every SHOW_POLL_MS to see it end.
// - Presenter view (Q3 = A, off in the first version, TODO Kazo-ca/Os-App-Deck#4): the window turns
//   it off when DeckCraft is ready (`show.presenter { on: false }`); this script turns it off again
//   if the Slide Show tab's toggle turned it on, and refuses the popup DeckCraft would open with
//   window.open, so nothing opens outside Kazo OS.
//
// Outside a show it costs one short burst of questions after a gesture (each answer is one frame
// of DeckCraft), never a timer. The state is written on <html data-deck-show>: "full-screen",
// "window" (the browser refused) or nothing, for the end-to-end runs.

/** Delays after a gesture at which the script asks whether a show started, in milliseconds. */
export const CHECKS_AFTER_GESTURE_MS = Object.freeze([60, 200, 450]);

/** How often the script asks whether a running show ended, in milliseconds. */
export const SHOW_POLL_MS = 500;

/** The fragment DeckCraft gives its presenter popup's address (apps/deckcraft-web/src/presenter.rs). */
export const PRESENTER_FRAGMENT = "#deckcraft-presenter=";

/**
 * Whether `url` is DeckCraft's presenter popup.
 * @param {unknown} url The first argument of window.open.
 * @returns {boolean}
 */
export function isPresenterUrl(url) {
    return String(url ?? "").includes(PRESENTER_FRAGMENT);
}

/**
 * Keeps window.open from opening DeckCraft's presenter popup (Q3 = A); every other address, such
 * as a link in a slide, opens as before. A refused popup returns null, as a blocked one does.
 * @param {Window} page The iframe page's window.
 */
export function refusePresenterPopup(page) {
    const open = page.open;
    page.open = function (url, ...rest) {
        return isPresenterUrl(url) ? null : open.call(this, url, ...rest);
    };
}

/**
 * Starts following DeckCraft's slide show.
 * @param {object} options
 * @param {Window} options.page The iframe page's window: gestures, timers, and DeckCraft's API (page.deckcraft).
 * @param {Document} options.document The page's document: full screen and the state attribute.
 * @param {Element} options.canvas DeckCraft's canvas, the element put in full screen.
 * @returns {{ stop(): void, check(): Promise<void> }}
 */
export function followSlideShow({ page, document, canvas }) {
    let showing = false;     // A show runs, as last seen.
    let tried = false;       // Full screen was asked for during this show.
    let ending = false;      // The show ended: the script itself leaves full screen.
    let checks = [];         // Timers of the questions after a gesture.
    let poll = 0;            // Timer of the questions during a show.
    let asking = false;      // A question is on its way: never two at once.

    const setState = (state) => {
        if (state) {
            document.documentElement.dataset.deckShow = state;
        } else {
            delete document.documentElement.dataset.deckShow;
        }
    };

    const execute = async (command, params) => {
        try {
            await page.deckcraft?.execute(command, params);
        } catch {
            // DeckCraft is not running (any more): nothing to end or to turn off.
        }
    };

    const isFullScreen = () => document.fullscreenElement === canvas;

    const enterFullScreen = async () => {
        tried = true;
        setState("window");
        if (typeof canvas.requestFullscreen !== "function") {
            return;
        }
        try {
            await canvas.requestFullscreen({ navigationUI: "hide" });
        } catch {
            // Refused (the gesture is too old, or the browser does not allow it): the show fills
            // the window, as DeckCraft draws it.
            return;
        }
        if (showing && isFullScreen()) {
            setState("full-screen");
        }
    };

    const startPolling = () => {
        if (!poll) {
            poll = page.setInterval(() => void check(), SHOW_POLL_MS);
        }
    };

    const stopPolling = () => {
        if (poll) {
            page.clearInterval(poll);
            poll = 0;
        }
    };

    const started = () => {
        showing = true;
        tried = false;
        startPolling();
    };

    const ended = () => {
        showing = false;
        tried = false;
        stopPolling();
        setState(null);
        if (isFullScreen()) {
            ending = true;
            Promise.resolve(document.exitFullscreen?.()).catch(() => {
                ending = false;
            });
        }
    };

    /** Asks DeckCraft whether a show runs, and acts on the change. */
    async function check() {
        const deckcraft = page.deckcraft;
        if (asking || typeof deckcraft?.uiInspect !== "function") {
            return;
        }
        asking = true;
        let ui;
        try {
            ui = await deckcraft.uiInspect();
        } catch {
            return;
        } finally {
            asking = false;
        }
        if (ui === null || typeof ui !== "object") {
            return;
        }

        if (ui.ui?.presenterView === true) {
            // Turned on with the Slide Show tab's toggle: the popup would be refused (Q3 = A), so
            // it goes back off, for this show's next start and the ones after.
            void execute("show.presenter", { on: false });
        }

        const now = ui.showing !== null && ui.showing !== undefined;
        if (now && !showing) {
            started();
        } else if (!now && showing) {
            ended();
        }

        // Full screen once per show, while a gesture allows it. A show that started without one
        // (the window's command, ?show) goes to full screen on the next gesture.
        if (showing && !tried && !isFullScreen() && page.navigator?.userActivation?.isActive !== false) {
            await enterFullScreen();
        }
    }

    // A burst of questions after a gesture; a gesture during a burst does not start another one.
    const onGesture = (event) => {
        if (event.isTrusted === false || checks.length > 0) {
            return;
        }
        checks = CHECKS_AFTER_GESTURE_MS.map((delay, index) => page.setTimeout(() => {
            if (index === CHECKS_AFTER_GESTURE_MS.length - 1) {
                checks = [];
            }
            void check();
        }, delay));
    };

    const onFullScreenChange = () => {
        if (isFullScreen()) {
            return;
        }
        if (ending) {
            ending = false;
            return;
        }
        if (showing && tried) {
            // Esc, or the browser's own way out: the show ends with full screen.
            void execute("show.end", {});
            ended();
        }
    };

    page.addEventListener("keydown", onGesture, true);
    page.addEventListener("pointerup", onGesture, true);
    document.addEventListener("fullscreenchange", onFullScreenChange);

    return {
        check,
        stop() {
            page.removeEventListener("keydown", onGesture, true);
            page.removeEventListener("pointerup", onGesture, true);
            document.removeEventListener("fullscreenchange", onFullScreenChange);
            checks.forEach((timer) => page.clearTimeout(timer));
            checks = [];
            stopPolling();
        },
    };
}

// In the iframe page (not in the tests, which import the functions).
if (typeof document !== "undefined" && typeof window !== "undefined") {
    const canvas = document.getElementById("deckcraft_canvas");
    if (canvas) {
        refusePresenterPopup(window);
        followSlideShow({ page: window, document, canvas });
    }
}
