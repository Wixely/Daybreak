let completedSectionObserver;

export function observeCompletedSection(section) {
    disconnectCompletedSection();
    section.classList.remove("is-revealed");
    const revealTarget = section.querySelector(".activity-card") ?? section;

    completedSectionObserver = new IntersectionObserver(entries => {
        const sectionEntry = entries[0];
        if (!sectionEntry?.isIntersecting || sectionEntry.intersectionRatio < 0.45) {
            return;
        }

        section.classList.add("is-revealed");
        disconnectCompletedSection();
    }, { threshold: [0, 0.45] });

    completedSectionObserver.observe(revealTarget);
}

export function disconnectCompletedSection() {
    completedSectionObserver?.disconnect();
    completedSectionObserver = undefined;
}
