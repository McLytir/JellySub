/* JellySub – client integration */
const ROOT_ID = 'jellysub-main-actions';
const POLL_MS = 1500;

(function init() {
    if (window.__jellySubClientInit) return;
    window.__jellySubClientInit = true;

    const scheduleRender = debounce(render, 120);
    const observer = new MutationObserver(scheduleRender);

    if (document.body) {
        observer.observe(document.body, { childList: true, subtree: true });
    } else {
        window.addEventListener('DOMContentLoaded', () => {
            observer.observe(document.body, { childList: true, subtree: true });
            render();
        }, { once: true });
    }

    window.addEventListener('hashchange', render);
    window.addEventListener('popstate', render);
    setInterval(render, POLL_MS);
    render();
})();

export default function () {}

function render() {
    const route = getRouteContext();
    const mount = findMountPoint();
    const existing = document.getElementById(ROOT_ID);

    if (!mount || (!route.itemId && !route.libraryId)) {
        existing?.remove();
        return;
    }

    if (existing && existing.parentElement !== mount) {
        existing.remove();
    }

    const root = existing || document.createElement('div');
    root.id = ROOT_ID;
    root.style.cssText = [
        'display:flex',
        'gap:8px',
        'flex-wrap:wrap',
        'align-items:center',
        'margin:12px 0'
    ].join(';');

    root.innerHTML = '';

    if (route.itemId) {
        root.appendChild(makeButton('💬 Subtitle search', () => {
            navigateTo(`configurationpage?name=jellysubsearch&itemId=${encodeURIComponent(route.itemId)}`);
        }));

        root.appendChild(makeButton('📂 Batch subtitles', () => {
            navigateTo(`configurationpage?name=jellysubseries&itemId=${encodeURIComponent(route.itemId)}`);
        }, true));
    }

    if (route.libraryId) {
        root.appendChild(makeButton('🔄 Library subtitles scan', () => {
            navigateTo(`configurationpage?name=jellysubscan&libraryId=${encodeURIComponent(route.libraryId)}`);
        }, true));
    }

    if (!root.parentElement) {
        mount.prepend(root);
    }
}

function findMountPoint() {
    const selectors = [
        '.detailPagePrimaryContainer',
        '.detailPageContent',
        '.itemDetailPage .content-primary',
        '.page[type="detail"] .content-primary',
        '.libraryPage .content-primary',
        '.listPage .content-primary',
        '.viewSettingsItems',
        '.mainAnimatedPage .content-primary'
    ];

    for (const selector of selectors) {
        const el = document.querySelector(selector);
        if (el) return el;
    }

    return null;
}

function getRouteContext() {
    const raw = `${window.location.pathname}${window.location.search}${window.location.hash}`;
    const parsed = readParams(raw);
    const itemId = parsed.id || parsed.itemId || '';
    const libraryId = parsed.topParentId || parsed.parentId || parsed.libraryId || '';
    return { itemId, libraryId };
}

function readParams(raw) {
    const out = {};
    const parts = raw.split('?').slice(1);

    for (const part of parts) {
        const clean = part.split('#')[0];
        const params = new URLSearchParams(clean);
        for (const [key, value] of params.entries()) {
            out[key] = value;
        }
    }

    const hash = window.location.hash || '';
    const hashQuery = hash.includes('?') ? hash.slice(hash.indexOf('?') + 1) : '';
    if (hashQuery) {
        const params = new URLSearchParams(hashQuery);
        for (const [key, value] of params.entries()) {
            out[key] = value;
        }
    }

    return out;
}

function navigateTo(route) {
    if (window.Dashboard?.navigate) {
        window.Dashboard.navigate(route);
        return;
    }

    window.location.href = `/web/index.html#!/${route}`;
}

function makeButton(label, onClick, alt = false) {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = `raised${alt ? ' button-alt' : ''}`;
    btn.textContent = label;
    btn.style.cssText = 'padding:8px 14px;border-radius:999px;cursor:pointer';
    btn.addEventListener('click', onClick);
    return btn;
}

function debounce(fn, wait) {
    let timer = null;
    return () => {
        clearTimeout(timer);
        timer = setTimeout(fn, wait);
    };
}
