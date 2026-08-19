let completedSectionObserver;
let idleTimer;
let idleTimeout = 60_000;
const activityEvents = ["pointerdown", "keydown", "wheel", "touchstart", "scroll"];

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
    stopIdleScroll();
}
