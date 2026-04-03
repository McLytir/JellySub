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
            this.liveSubtitleSettings = this.loadLiveSubtitleSettings();

            this.onClickCapture = this.onClickCapture.bind(this);
            this.observeSheets = this.observeSheets.bind(this);
            this.injectHeaderActions = this.injectHeaderActions.bind(this);
            this.injectPlaybackControls = this.injectPlaybackControls.bind(this);

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

            this.injectSubtitleStyleSheet();
            this.applyLiveSubtitleSettings();
            this.headerTimer = window.setInterval(() => {
                this.injectHeaderActions();
                this.injectPlaybackControls();
            }, 1200);
            this.observeSheets();
            this.injectHeaderActions();
            this.injectPlaybackControls();
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

        injectPlaybackControls() {
            const hasVideo = !!document.querySelector('video');
            const existing = document.getElementById('jellysub-live-subtitle-controls');
            if (!hasVideo) {
                if (existing) {
                    existing.remove();
                }
                return;
            }

            const root = existing || document.createElement('div');
            root.id = 'jellysub-live-subtitle-controls';
            root.style.cssText = 'position:fixed;right:16px;bottom:16px;z-index:99999;background:rgba(20,20,20,.88);color:#fff;padding:12px;border-radius:12px;box-shadow:0 8px 24px rgba(0,0,0,.35);width:min(280px,calc(100vw - 32px));font-size:12px';
            root.innerHTML = `
                <div style="display:flex;align-items:center;justify-content:space-between;gap:8px;margin-bottom:8px">
                    <strong>JellySub live subtitles</strong>
                    <button type="button" data-jellysub-live-toggle style="background:none;border:none;color:#fff;cursor:pointer;font-size:18px;line-height:1">–</button>
                </div>
                <div data-jellysub-live-body>
                    <label style="display:block;margin-bottom:8px">
                        <span style="display:block;color:#bbb;margin-bottom:4px">Font family</span>
                        <input type="text" data-jellysub-live-font value="${escapeHtml(this.liveSubtitleSettings.fontFamily)}" style="width:100%;padding:6px 8px;border-radius:8px;border:1px solid rgba(255,255,255,.15);background:rgba(255,255,255,.08);color:#fff" />
                    </label>
                    <label style="display:block;margin-bottom:8px">
                        <span data-jellysub-live-size-label style="display:block;color:#bbb;margin-bottom:4px">Subtitle size (${this.liveSubtitleSettings.scalePercent}%)</span>
                        <input type="range" min="60" max="220" step="5" data-jellysub-live-size value="${this.liveSubtitleSettings.scalePercent}" style="width:100%" />
                    </label>
                    <div style="display:flex;gap:8px;justify-content:flex-end">
                        <button type="button" data-jellysub-live-reset class="raised button-alt" style="padding:6px 10px;border-radius:999px;cursor:pointer">Reset</button>
                    </div>
                </div>`;

            const fontInput = root.querySelector('[data-jellysub-live-font]');
            const sizeInput = root.querySelector('[data-jellysub-live-size]');
            const sizeLabel = root.querySelector('[data-jellysub-live-size-label]');
            const body = root.querySelector('[data-jellysub-live-body]');
            const toggle = root.querySelector('[data-jellysub-live-toggle]');
            toggle.addEventListener('click', () => {
                const collapsed = body.style.display === 'none';
                body.style.display = collapsed ? 'block' : 'none';
                toggle.textContent = collapsed ? '–' : '+';
            });
            root.querySelector('[data-jellysub-live-reset]').addEventListener('click', () => {
                this.liveSubtitleSettings = { fontFamily: 'Arial', scalePercent: 100 };
                fontInput.value = this.liveSubtitleSettings.fontFamily;
                sizeInput.value = String(this.liveSubtitleSettings.scalePercent);
                sizeLabel.textContent = `Subtitle size (${this.liveSubtitleSettings.scalePercent}%)`;
                this.applyLiveSubtitleSettings();
                this.saveLiveSubtitleSettings();
            });
            fontInput.addEventListener('input', () => {
                this.liveSubtitleSettings.fontFamily = fontInput.value.trim() || 'Arial';
                this.applyLiveSubtitleSettings();
                this.saveLiveSubtitleSettings();
            });
            sizeInput.addEventListener('input', () => {
                this.liveSubtitleSettings.scalePercent = parseInt(sizeInput.value, 10) || 100;
                sizeLabel.textContent = `Subtitle size (${this.liveSubtitleSettings.scalePercent}%)`;
                this.applyLiveSubtitleSettings();
                this.saveLiveSubtitleSettings();
            });

            if (!root.parentElement) {
                document.body.appendChild(root);
            }
        }

        injectSubtitleStyleSheet() {
            if (document.getElementById('jellysub-live-subtitle-style')) {
                return;
            }

            const style = document.createElement('style');
            style.id = 'jellysub-live-subtitle-style';
            style.textContent = `
                :root {
                    --jellysub-live-font-family: Arial;
                    --jellysub-live-font-size: 100%;
                }
                video::cue {
                    font-family: var(--jellysub-live-font-family) !important;
                    font-size: var(--jellysub-live-font-size) !important;
                }
                .subtitleText,
                .videoSubtitles,
                .playerSubtitleText,
                .vjs-text-track-display div,
                .libassjs-canvas-parent {
                    font-family: var(--jellysub-live-font-family) !important;
                    font-size: var(--jellysub-live-font-size) !important;
                }
            `;
            document.head.appendChild(style);
        }

        loadLiveSubtitleSettings() {
            try {
                const raw = window.localStorage.getItem('jellysub-live-subtitle-settings');
                if (!raw) {
                    return { fontFamily: 'Arial', scalePercent: 100 };
                }
                const parsed = JSON.parse(raw);
                return {
                    fontFamily: String(parsed.fontFamily || 'Arial'),
                    scalePercent: Math.max(60, Math.min(220, parseInt(parsed.scalePercent, 10) || 100)),
                };
            } catch (_) {
                return { fontFamily: 'Arial', scalePercent: 100 };
            }
        }

        saveLiveSubtitleSettings() {
            try {
                window.localStorage.setItem('jellysub-live-subtitle-settings', JSON.stringify(this.liveSubtitleSettings));
            } catch (_) {}
        }

        applyLiveSubtitleSettings() {
            const root = document.documentElement;
            root.style.setProperty('--jellysub-live-font-family', this.liveSubtitleSettings.fontFamily || 'Arial');
            root.style.setProperty('--jellysub-live-font-size', `${this.liveSubtitleSettings.scalePercent || 100}%`);
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
                        run: () => this.navigate(`configurationpage?name=jellysubsearchv4&op=single&mode=assisted&itemId=${encodeURIComponent(context.itemId)}`)
                    });
                }

                if (isFolderLike || isVideoLike) {
                    actions.push({
                        id: 'jellysub-batch',
                        icon: 'folder',
                        name: 'JellySub: Batch subtitles',
                        compactName: '📂 Batch subtitles',
                        run: () => this.navigate(`configurationpage?name=jellysubsearchv4&op=batch&itemId=${encodeURIComponent(context.itemId)}`)
                    });
                }
            }

            if (context.libraryId && headerMode) {
                actions.push({
                    id: 'jellysub-scan',
                    icon: 'manage_search',
                    name: 'JellySub: Library subtitle scan',
                    compactName: '🔄 Subtitle scan',
                    run: () => this.navigate(`configurationpage?name=jellysubsearchv4&op=scan&libraryId=${encodeURIComponent(context.libraryId)}`)
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
