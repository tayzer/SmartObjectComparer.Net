window.parityBenchDownloadText = (fileName, contentType, content) => {
    const blob = new Blob([content ?? ""], { type: contentType || "text/plain" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName || "comparison-report.txt";
    anchor.style.display = "none";
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
};

window.parityBenchScrollToElement = (id) => {
    const element = document.getElementById(id);
    if (element) {
        element.scrollIntoView({ behavior: "smooth", block: "start" });
    }
};

window.parityBenchSetSyncedScroll = (leftId, rightId, enabled) => {
    const left = document.getElementById(leftId);
    const right = document.getElementById(rightId);
    const stateKey = "__parityBenchSyncedScroll";

    const detach = (element) => {
        const state = element?.[stateKey];
        if (!state) {
            return;
        }

        element.removeEventListener("scroll", state.handler);
        delete element[stateKey];
    };

    detach(left);
    detach(right);

    if (!enabled || !left || !right) {
        return;
    }

    let isSyncing = false;
    const bind = (source, target) => {
        const handler = () => {
            if (isSyncing) {
                return;
            }

            isSyncing = true;
            const topRange = Math.max(1, source.scrollHeight - source.clientHeight);
            const leftRange = Math.max(1, source.scrollWidth - source.clientWidth);
            const targetTopRange = Math.max(0, target.scrollHeight - target.clientHeight);
            const targetLeftRange = Math.max(0, target.scrollWidth - target.clientWidth);

            target.scrollTop = (source.scrollTop / topRange) * targetTopRange;
            target.scrollLeft = (source.scrollLeft / leftRange) * targetLeftRange;
            window.requestAnimationFrame(() => { isSyncing = false; });
        };

        source[stateKey] = { handler };
        source.addEventListener("scroll", handler, { passive: true });
    };

    bind(left, right);
    bind(right, left);
};