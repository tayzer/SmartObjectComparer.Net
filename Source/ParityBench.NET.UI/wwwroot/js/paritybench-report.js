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
window.parityBenchTheme = {
    getStoredMode: () => {
        try {
            return window.localStorage.getItem("paritybench.themeMode");
        } catch {
            return null;
        }
    },
    setStoredMode: (mode) => {
        try {
            if (!mode) {
                window.localStorage.removeItem("paritybench.themeMode");
                return;
            }

            window.localStorage.setItem("paritybench.themeMode", mode);
        } catch {
        }
    },
    getSystemPrefersDark: () => {
        try {
            return window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches;
        } catch {
            return false;
        }
    }
};
window.parityBenchReplaceUrl = (url) => {
    try {
        window.history.replaceState(window.history.state, "", url);
    } catch {
    }
};

window.parityBenchScrollToElement = (id) => {
    const element = document.getElementById(id);
    if (element) {
        element.scrollIntoView({ behavior: "smooth", block: "start" });
    }
};

window.parityBenchSetSyncedScroll = (leftId, rightId, enabled) => {
    const stateKey = "__parityBenchSyncedScroll";
    const scheduleKey = "__parityBenchSyncedScrollSchedules";
    window[scheduleKey] = window[scheduleKey] || new Map();
    const scheduleId = `${leftId}|${rightId}`;

    const clearScheduledBind = () => {
        const timeoutId = window[scheduleKey].get(scheduleId);
        if (timeoutId) {
            window.clearTimeout(timeoutId);
            window[scheduleKey].delete(scheduleId);
        }
    };

    const detach = element => {
        const state = element?.[stateKey];
        if (!state) {
            return;
        }

        element.removeEventListener("scroll", state.handler);
        delete element[stateKey];
    };

    const configure = () => {
        const left = document.getElementById(leftId);
        const right = document.getElementById(rightId);

        detach(left);
        detach(right);

        if (!enabled || !left || !right) {
            return false;
        }

        let isSyncing = false;
        const getRows = element => Array.from(element.querySelectorAll("[data-diff-row]"));
        const getHeaderHeight = element => element.querySelector(".aligned-diff-header")?.offsetHeight ?? 0;
        const findTopRow = (element, rows) => {
            if (rows.length === 0) {
                return null;
            }

            const anchorTop = element.scrollTop + getHeaderHeight(element);
            if (anchorTop < rows[0].offsetTop) {
                return null;
            }

            let low = 0;
            let high = rows.length - 1;
            let result = rows[0];
            while (low <= high) {
                const middle = Math.floor((low + high) / 2);
                const row = rows[middle];
                if (row.offsetTop <= anchorTop) {
                    result = row;
                    low = middle + 1;
                } else {
                    high = middle - 1;
                }
            }

            return result;
        };
        const syncVerticalPosition = (source, target) => {
            const sourceRows = getRows(source);
            const targetRows = getRows(target);
            const sourceRow = findTopRow(source, sourceRows);
            if (!sourceRow || targetRows.length === 0) {
                const sourceRange = Math.max(1, source.scrollHeight - source.clientHeight);
                const targetRange = Math.max(0, target.scrollHeight - target.clientHeight);
                target.scrollTop = (source.scrollTop / sourceRange) * targetRange;
                return;
            }

            const rowIndex = Number.parseInt(sourceRow.dataset.diffRow ?? "0", 10);
            const targetRow = targetRows.find(row => Number.parseInt(row.dataset.diffRow ?? "-1", 10) === rowIndex);
            if (!targetRow) {
                return;
            }

            const sourceRowHeight = Math.max(1, sourceRow.offsetHeight);
            const sourceAnchorTop = source.scrollTop + getHeaderHeight(source);
            const progress = Math.max(0, Math.min(1, (sourceAnchorTop - sourceRow.offsetTop) / sourceRowHeight));
            const targetTop = targetRow.offsetTop - getHeaderHeight(target) + (progress * Math.max(1, targetRow.offsetHeight));
            target.scrollTop = Math.max(0, targetTop);
        };
        const bind = (source, target) => {
            const handler = () => {
                if (isSyncing) {
                    return;
                }

                isSyncing = true;
                const leftRange = Math.max(1, source.scrollWidth - source.clientWidth);
                const targetLeftRange = Math.max(0, target.scrollWidth - target.clientWidth);

                syncVerticalPosition(source, target);
                target.scrollLeft = (source.scrollLeft / leftRange) * targetLeftRange;
                window.requestAnimationFrame(() => { isSyncing = false; });
            };

            source[stateKey] = { handler };
            source.addEventListener("scroll", handler, { passive: true });
        };

        bind(left, right);
        bind(right, left);
        return true;
    };

    clearScheduledBind();
    const configured = configure();
    if (!enabled) {
        return;
    }

    window.requestAnimationFrame(() => {
        window.requestAnimationFrame(() => {
            configure();
            const timeoutId = window.setTimeout(() => {
                configure();
                window[scheduleKey].delete(scheduleId);
            }, configured ? 150 : 50);
            window[scheduleKey].set(scheduleId, timeoutId);
        });
    });
};
