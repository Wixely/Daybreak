let completedSectionObserver;
let idleTimer;
let idleTimeout = 60_000;
let fullscreenButton;
const activityEvents = ["pointerdown", "keydown", "wheel", "touchstart", "scroll"];
const keepAwakeStorageKey = "daybreak.keepAwake";
const keepAwakeActivationEvents = ["pointerdown", "touchstart", "keydown"];
const boundKeepAwakeToggles = new WeakSet();
let keepAwakeAudio;
let keepAwakeAudioUrl;
let keepAwakeTimer;
let keepAwakeRetryListening = false;
let keepAwakeEnabled;

export function observeCompletedSection(section) {
    disconnectCompletedSection();
    section.classList.remove("is-revealed");
    const revealTarget = section.querySelector(".activity-card") ?? section;

    completedSectionObserver = new IntersectionObserver(entries => {
        const sectionEntry = entries[0];
        const isRevealed = sectionEntry?.isIntersecting && sectionEntry.intersectionRatio >= 0.45;
        section.classList.toggle("is-revealed", isRevealed);
    }, { threshold: [0, 0.45] });

    completedSectionObserver.observe(revealTarget);
}

export function disconnectCompletedSection() {
    completedSectionObserver?.disconnect();
    completedSectionObserver = undefined;
}

export function startIdleScroll(timeout = 60_000) {
    stopIdleScroll();
    idleTimeout = timeout;
    for (const eventName of activityEvents) {
        window.addEventListener(eventName, resetIdleTimer, { passive: true });
    }

    resetIdleTimer();
}

export function initializeKeepAwake() {
    if (readKeepAwakePreference()) {
        void startKeepAwake();
    }
}

export async function copyText(value) {
    if (navigator.clipboard?.writeText) {
        try {
            await navigator.clipboard.writeText(value);
            return;
        } catch {
            // Fall through for trusted-network HTTP deployments without clipboard permission.
        }
    }

    const input = document.createElement("textarea");
    input.value = value;
    input.setAttribute("readonly", "");
    input.style.position = "fixed";
    input.style.opacity = "0";
    document.body.appendChild(input);
    input.select();
    const copied = document.execCommand("copy");
    input.remove();
    if (!copied) {
        throw new Error("Clipboard access is unavailable in this browser.");
    }
}

export function bindFullscreenButton(button) {
    disconnectFullscreenButton();
    if (!button) {
        return;
    }

    fullscreenButton = button;
    fullscreenButton.addEventListener("click", requestDashboardFullscreen);
    document.addEventListener("fullscreenchange", updateFullscreenButton);
    updateFullscreenButton();
}

function isMobileDevice() {
    if (navigator.userAgentData?.mobile === true) {
        return true;
    }

    if (/Android|iPhone|iPad|iPod|IEMobile|Opera Mini/i.test(navigator.userAgent)) {
        return true;
    }

    return navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1;
}

function canRequestDashboardFullscreen() {
    return isMobileDevice()
        && window.self === window.top
        && document.fullscreenEnabled !== false
        && typeof document.documentElement.requestFullscreen === "function"
        && !document.fullscreenElement;
}

function updateFullscreenButton() {
    if (fullscreenButton) {
        fullscreenButton.hidden = !canRequestDashboardFullscreen();
    }
}

async function requestDashboardFullscreen() {
    if (readKeepAwakePreference()) {
        // Start playback directly inside the fullscreen click gesture. Mobile browsers
        // may reject the earlier automatic attempt even though the preference is enabled.
        void startKeepAwake();
    }

    if (!canRequestDashboardFullscreen()) {
        updateFullscreenButton();
        return;
    }

    try {
        await document.documentElement.requestFullscreen();
    } catch {
        // A browser or device policy may reject fullscreen even when the API is exposed.
    }

    updateFullscreenButton();
}

function disconnectFullscreenButton() {
    if (!fullscreenButton) {
        return;
    }

    fullscreenButton.removeEventListener("click", requestDashboardFullscreen);
    document.removeEventListener("fullscreenchange", updateFullscreenButton);
    fullscreenButton = undefined;
}

export function bindKeepAwakeSetting(toggle) {
    if (!toggle) {
        return;
    }

    toggle.checked = readKeepAwakePreference();
    if (toggle.checked) {
        void startKeepAwake();
    }

    if (boundKeepAwakeToggles.has(toggle)) {
        return;
    }

    boundKeepAwakeToggles.add(toggle);
    toggle.addEventListener("change", () => {
        writeKeepAwakePreference(toggle.checked);
        if (toggle.checked) {
            void startKeepAwake();
        } else {
            stopKeepAwake();
        }
    });
}

function readKeepAwakePreference() {
    if (keepAwakeEnabled !== undefined) {
        return keepAwakeEnabled;
    }

    try {
        keepAwakeEnabled = window.localStorage.getItem(keepAwakeStorageKey) === "true";
    } catch {
        keepAwakeEnabled = false;
    }

    return keepAwakeEnabled;
}

function writeKeepAwakePreference(enabled) {
    keepAwakeEnabled = enabled;
    try {
        window.localStorage.setItem(keepAwakeStorageKey, enabled ? "true" : "false");
    } catch {
        // The feature remains usable for this page even when storage is unavailable.
    }
}

async function startKeepAwake() {
    if (!readKeepAwakePreference()) {
        return;
    }

    if (!keepAwakeAudio) {
        keepAwakeAudioUrl = createSilentWaveUrl();
        keepAwakeAudio = document.createElement("audio");
        keepAwakeAudio.src = keepAwakeAudioUrl;
        keepAwakeAudio.loop = true;
        keepAwakeAudio.preload = "auto";
        keepAwakeAudio.setAttribute("aria-hidden", "true");
        keepAwakeAudio.style.display = "none";
        document.body.appendChild(keepAwakeAudio);
    }

    try {
        await keepAwakeAudio.play();
        stopKeepAwakeRetry();
        if (!keepAwakeTimer) {
            keepAwakeTimer = window.setInterval(() => {
                if (!readKeepAwakePreference()) {
                    stopKeepAwake();
                    return;
                }

                keepAwakeAudio.currentTime = 0;
                void keepAwakeAudio.play().catch(startKeepAwakeRetry);
            }, 55_000);
        }
    } catch {
        startKeepAwakeRetry();
    }
}

function startKeepAwakeRetry() {
    if (keepAwakeRetryListening) {
        return;
    }

    keepAwakeRetryListening = true;
    for (const eventName of keepAwakeActivationEvents) {
        window.addEventListener(eventName, retryKeepAwake, { passive: true });
    }
}

function retryKeepAwake() {
    stopKeepAwakeRetry();
    void startKeepAwake();
}

function stopKeepAwakeRetry() {
    if (!keepAwakeRetryListening) {
        return;
    }

    keepAwakeRetryListening = false;
    for (const eventName of keepAwakeActivationEvents) {
        window.removeEventListener(eventName, retryKeepAwake);
    }
}

function stopKeepAwake() {
    window.clearInterval(keepAwakeTimer);
    keepAwakeTimer = undefined;
    stopKeepAwakeRetry();
    if (keepAwakeAudio) {
        keepAwakeAudio.pause();
        keepAwakeAudio.remove();
        keepAwakeAudio = undefined;
    }

    if (keepAwakeAudioUrl) {
        URL.revokeObjectURL(keepAwakeAudioUrl);
        keepAwakeAudioUrl = undefined;
    }
}

function createSilentWaveUrl() {
    const sampleRate = 8_000;
    const sampleCount = sampleRate;
    const buffer = new ArrayBuffer(44 + sampleCount * 2);
    const view = new DataView(buffer);
    writeAscii(view, 0, "RIFF");
    view.setUint32(4, 36 + sampleCount * 2, true);
    writeAscii(view, 8, "WAVE");
    writeAscii(view, 12, "fmt ");
    view.setUint32(16, 16, true);
    view.setUint16(20, 1, true);
    view.setUint16(22, 1, true);
    view.setUint32(24, sampleRate, true);
    view.setUint32(28, sampleRate * 2, true);
    view.setUint16(32, 2, true);
    view.setUint16(34, 16, true);
    writeAscii(view, 36, "data");
    view.setUint32(40, sampleCount * 2, true);
    return URL.createObjectURL(new Blob([buffer], { type: "audio/wav" }));
}

function writeAscii(view, offset, value) {
    for (let index = 0; index < value.length; index++) {
        view.setUint8(offset + index, value.charCodeAt(index));
    }
}

function resetIdleTimer() {
    window.clearTimeout(idleTimer);
    idleTimer = window.setTimeout(() => {
        const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
        window.scrollTo({ top: 0, behavior: reduceMotion ? "auto" : "smooth" });
    }, idleTimeout);
}

function stopIdleScroll() {
    window.clearTimeout(idleTimer);
    idleTimer = undefined;
    for (const eventName of activityEvents) {
        window.removeEventListener(eventName, resetIdleTimer);
    }
}

export function stopDashboardBehavior() {
    disconnectCompletedSection();
    disconnectFullscreenButton();
    stopIdleScroll();
}
