/* JellySub web-client plugin
 *
 * Install into a Jellyfin web root by:
 *   1. copying this file to <webroot>/jellysub-context-plugin.js
 *   2. loading it from index.html before the main app bootstraps
 *   3. adding "jellysubContext" to config.json.plugins
 *
 * The plugin exposes a window global so Jellyfin's pluginManager can load it
 * through the "window plugin" code path.
 */
(function () {
    if (window.jellysubContext) {
        return;
    }

    class JellySubContextPlugin {
        constructor(deps) {
            this.name = 'JellySub Context Actions';
            this.id = 'jellysubContext';
            this.type = 'general';
            this.priority = 0;

            this.dashboard = deps?.dashboard || window.Dashboard;
            this.toast = deps?.toast || { show: () => {}, default: () => {} };

            this.lastClickTarget = null;
            this.lastContext = null;
            this.sheetObserver = null;
            this.headerTimer = null;

            this.onClickCapture = this.onClickCapture.bind(this);
            this.observeSheets = this.observeSheets.bind(this);
            this.injectHeaderActions = this.injectHeaderActions.bind(this);

            this.init();
        }

        init() {
            document.addEventListener('click', this.onClickCapture, true);

            this.sheetObserver = new MutationObserver(this.observeSheets);
            if (document.body) {
                this.sheetObserver.observe(document.body, { childList: true, subtree: true });
            } else {
                window.addEventListener('DOMContentLoaded', () => {
                    this.sheetObserver.observe(document.body, { childList: true, subtree: true });
                }, { once: true });
            }

            this.headerTimer = window.setInterval(this.injectHeaderActions, 1200);
            this.observeSheets();
            this.injectHeaderActions();
        }

        onClickCapture(event) {
            const target = event.target instanceof Element ? event.target : null;
            this.lastClickTarget = target;
            this.lastContext = this.getContextFromTarget(target) || this.getContextFromRoute();
        }

        observeSheets() {
            const scrollers = document.querySelectorAll('.actionSheetScroller');
            for (const scroller of scrollers) {
                this.injectIntoActionSheet(scroller);
            }
        }

        injectIntoActionSheet(scroller) {
            if (!(scroller instanceof HTMLElement)) return;
            if (scroller.dataset.jellysubPatched === 'true') return;

            const context = this.lastContext || this.getContextFromRoute();
            const actions = this.getAvailableActions(context);
            if (!actions.length) return;

            const hasExisting = Array.from(scroller.querySelectorAll('.actionSheetMenuItem'))
                .some(el => (el.textContent || '').toLowerCase().includes('jellysub'));
            if (hasExisting) {
                scroller.dataset.jellysubPatched = 'true';
                return;
            }

            const divider = document.createElement('div');
            divider.className = 'actionsheetDivider';
            scroller.appendChild(divider);

            for (const action of actions) {
                scroller.appendChild(this.buildActionSheetButton(action));
            }

            scroller.dataset.jellysubPatched = 'true';
        }

        buildActionSheetButton(action) {
            const button = document.createElement('button');
            button.setAttribute('is', 'emby-button');
            button.type = 'button';
            button.className = 'listItem listItem-button actionSheetMenuItem listItem-border';
            button.dataset.id = action.id;
            button.innerHTML = [
                `<span class="actionsheetMenuItemIcon listItemIcon listItemIcon-transparent material-icons ${action.icon}" aria-hidden="true"></span>`,
                '<div class="listItemBody actionsheetListItemBody">',
                `<div class="listItemBodyText actionSheetItemText">${escapeHtml(action.name)}</div>`,
                '</div>'
            ].join('');

            button.addEventListener('click', (event) => {
                event.preventDefault();
                event.stopPropagation();
                this.handleAction(action);

                const dialog = button.closest('.dialog') || button.closest('.actionSheet') || button.closest('[role="dialog"]');
                if (dialog && typeof dialog.close === 'function') {
                    try { dialog.close(); } catch (_) {}
                }
            });

            return button;
        }

        injectHeaderActions() {
            const route = this.getContextFromRoute();
            const mount = this.findHeaderMount();
            const existing = document.getElementById('jellysub-header-actions');

            if (!mount || (!route.itemId && !route.libraryId)) {
                if (existing) existing.remove();
                return;
            }

            const root = existing || document.createElement('div');
            root.id = 'jellysub-header-actions';
            root.style.cssText = 'display:flex;gap:8px;flex-wrap:wrap;align-items:center;margin:12px 0';
            root.innerHTML = '';

            const actions = this.getAvailableActions(route, true);
            for (const action of actions) {
                const btn = document.createElement('button');
                btn.type = 'button';
                btn.className = 'raised button-alt';
                btn.style.cssText = 'padding:8px 14px;border-radius:999px;cursor:pointer';
                btn.textContent = action.compactName || action.name;
                btn.addEventListener('click', () => this.handleAction(action));
                root.appendChild(btn);
            }

            if (!root.parentElement) {
                mount.prepend(root);
            }
        }

        getAvailableActions(context, headerMode) {
            if (!context) return [];

            const actions = [];
            const itemType = String(context.type || '');
            const isFolderLike = !!context.isFolder || ['Series', 'Season', 'CollectionFolder', 'Folder', 'BoxSet'].includes(itemType);
            const isVideoLike = ['Movie', 'Episode', 'Video'].includes(itemType);

            if (context.itemId) {
                if (isVideoLike) {
                    actions.push({
                        id: 'jellysub-search',
                        icon: 'subtitles',
                        name: 'JellySub: Search subtitles',
                        compactName: '💬 Subtitle search',
                        run: () => this.navigate(`configurationpage?name=jellysubsearchv3&mode=assisted&itemId=${encodeURIComponent(context.itemId)}`)
                    });
                }

                if (isFolderLike || isVideoLike) {
                    actions.push({
                        id: 'jellysub-batch',
                        icon: 'folder',
                        name: 'JellySub: Batch subtitles',
                        compactName: '📂 Batch subtitles',
                        run: () => this.navigate(`configurationpage?name=jellysubseries&itemId=${encodeURIComponent(context.itemId)}`)
                    });
                }
            }

            if (context.libraryId && headerMode) {
                actions.push({
                    id: 'jellysub-scan',
                    icon: 'manage_search',
                    name: 'JellySub: Library subtitle scan',
                    compactName: '🔄 Subtitle scan',
                    run: () => this.navigate(`configurationpage?name=jellysubscan&libraryId=${encodeURIComponent(context.libraryId)}`)
                });
            }

            return actions;
        }

        handleAction(action) {
            try {
                action.run();
            } catch (error) {
                console.error('[JellySubContext] action failed', error);
            }
        }

        navigate(url) {
            if (this.dashboard && typeof this.dashboard.navigate === 'function') {
                this.dashboard.navigate(url);
                return;
            }

            window.location.href = '/' + url;
        }

        getContextFromTarget(target) {
            if (!(target instanceof Element)) return null;

            const card = target.closest('[data-id]');
            if (card) {
                return {
                    itemId: card.getAttribute('data-id') || '',
                    type: card.getAttribute('data-type') || '',
                    isFolder: card.getAttribute('data-isfolder') === 'true',
                    libraryId: this.getLibraryIdFromRoute()
                };
            }

            return this.getContextFromRoute();
        }

        getContextFromRoute() {
            const params = this.getRouteParams();
            const itemId = params.id || params.itemId || '';
            const libraryId = this.getLibraryIdFromParams(params);

            return {
                itemId,
                libraryId,
                type: params.type || '',
                isFolder: false
            };
        }

        getLibraryIdFromRoute() {
            return this.getLibraryIdFromParams(this.getRouteParams());
        }

        getLibraryIdFromParams(params) {
            return params.topParentId || params.parentId || params.libraryId || '';
        }

        getRouteParams() {
            const out = {};
            const sources = [window.location.search || '', window.location.hash || ''];

            for (const source of sources) {
                const query = source.includes('?') ? source.slice(source.indexOf('?') + 1) : source.replace(/^\?/, '');
                if (!query) continue;

                const params = new URLSearchParams(query);
                for (const [key, value] of params.entries()) {
                    out[key] = value;
                }
            }

            return out;
        }

        findHeaderMount() {
            const selectors = [
                '.detailPagePrimaryContainer',
                '.detailPageContent',
                '.mainDetailButtons',
                '.itemDetailPage .content-primary',
                '.libraryPage .content-primary',
                '.mainAnimatedPage .content-primary'
            ];

            for (const selector of selectors) {
                const element = document.querySelector(selector);
                if (element) return element;
            }

            return null;
        }
    }

    function escapeHtml(value) {
        return String(value).replace(/[&<>"']/g, (c) => ({
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#39;'
        }[c]));
    }

    window.jellysubContext = async () => JellySubContextPlugin;
})();
