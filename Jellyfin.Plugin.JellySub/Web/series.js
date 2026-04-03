/* JellySub – legacy batch route shim */

export default function (_view, params) {
    const routeParams = resolveParams(params);
    const target = new URLSearchParams({ name: 'jellysubsearchv3', op: 'batch' });

    if (routeParams.itemId) {
        target.set('itemId', routeParams.itemId);
    }
    if (routeParams.batchMode) {
        target.set('batchMode', routeParams.batchMode);
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
