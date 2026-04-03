/* JellySub – legacy search route shim */

export default function (_view, params) {
    const routeParams = resolveParams(params);
    const target = new URLSearchParams();
    target.set('name', 'jellysubsearchv4');

    for (const [key, value] of Object.entries(routeParams)) {
        if (key === 'name') {
            continue;
        }
        if (value !== undefined && value !== null && value !== '') {
            target.set(key, value);
        }
    }

    if (!target.has('op')) {
        target.set('op', 'single');
    }

    navigateTo(`configurationpage?${target.toString()}`);
}

function navigateTo(url) {
    if (Dashboard.navigate) {
        Dashboard.navigate(url);
        return;
    }

    window.location.href = '/' + url;
}

function resolveParams(params) {
    const resolved = { ...(params || {}) };
    const querySources = [window.location.search || '', window.location.hash || ''];

    for (const source of querySources) {
        const query = source.includes('?') ? source.slice(source.indexOf('?') + 1) : source.replace(/^\?/, '');
        if (!query) {
            continue;
        }

        const parsed = new URLSearchParams(query);
        for (const [key, value] of parsed.entries()) {
            if (resolved[key] === undefined || resolved[key] === null || resolved[key] === '') {
                resolved[key] = value;
            }
        }
    }

    return resolved;
}
