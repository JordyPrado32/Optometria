const storageKey = "optica-lux.nav-state.v1";

export function save(expandedMenuIds, scrollElement) {
    const state = {
        expandedMenuIds: Array.isArray(expandedMenuIds) ? expandedMenuIds : [],
        scrollTop: scrollElement?.scrollTop ?? 0
    };
    sessionStorage.setItem(storageKey, JSON.stringify(state));
}

export function restore() {
    try {
        const raw = sessionStorage.getItem(storageKey);
        return raw ? JSON.parse(raw) : null;
    } catch {
        return null;
    }
}

export function setScroll(scrollElement, scrollTop) {
    if (scrollElement) scrollElement.scrollTop = Number(scrollTop) || 0;
}
