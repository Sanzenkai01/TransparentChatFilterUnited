(() => {
    const config = window.__tcfTwitchDomConfig || (window.__tcfTwitchDomConfig = Object.freeze({
        messageContainer: [
            '[data-test-selector="chat-scrollable-area__message-container"]',
            '[data-a-target="chat-scrollable-area__message-container"]',
            '[role="log"].chat-scrollable-area__message-container'
        ].join(','),
        messageLine: [
            '[data-a-target="chat-line-message"]',
            '[data-test-selector="chat-line-message"]',
            '.chat-line__message'
        ].join(','),
        username: [
            '[data-a-target="chat-message-username"]',
            '[data-test-selector="message-username"]',
            '.chat-author__display-name'
        ].join(','),
        messageBody: [
            '[data-a-target="chat-line-message-body"]',
            '[data-test-selector="message-body"]',
            '.text-fragment'
        ].join(','),
        sharedChatImage: [
            '[data-a-target*="shared-chat"] img',
            '[data-test-selector*="shared-chat"] img',
            '[class*="shared-chat"] img',
            '[data-a-target*="sharedChat"] img',
            '[data-test-selector*="sharedChat"] img',
            '[data-a-target*="channel-badge"] img',
            '[data-test-selector*="channel-badge"] img',
            'img[data-a-target*="shared-chat"]',
            'img[data-test-selector*="shared-chat"]',
            'img[data-a-target*="sharedChat"]',
            'img[data-test-selector*="sharedChat"]'
        ].join(',')
    }));
    const styleId = 'transparent-chat-filter-messages-only';
    const css = `
        html, body { width:100%!important; height:100%!important; margin:0!important; overflow:hidden!important; background:transparent!important; }
        *, *::before, *::after { scrollbar-width:none!important; -ms-overflow-style:none!important; }
        *::-webkit-scrollbar { display:none!important; width:0!important; height:0!important; }
        body * { visibility:hidden!important; }
        ${config.messageContainer} {
            position:fixed!important; inset:0!important; z-index:2147483647!important;
            display:block!important; width:100vw!important; height:100vh!important;
            max-height:100vh!important; overflow-x:hidden!important; overflow-y:auto!important;
            scrollbar-width:none!important; -ms-overflow-style:none!important;
            margin:0!important; padding:0!important; background:transparent!important;
            visibility:hidden!important;
        }
        ${config.messageContainer} ${config.messageLine} {
            display:block!important;
            width:max-content!important;
            max-width:100%!important;
            box-sizing:border-box!important;
            margin:2px 0!important;
            padding:3px 7px!important;
            border-radius:4px!important;
            background:rgba(0,0,0,0.72)!important;
        }
        ${config.messageContainer} ${config.messageLine} * {
            background-color:transparent!important;
            -webkit-text-stroke:0!important;
            text-shadow:0 1px 1px rgba(0,0,0,0.95), 0 0 3px rgba(0,0,0,0.8)!important;
            visibility:hidden!important;
        }
    `;
    let observedContainer = null;
    let containerObserver = null;
    let reconciliationTimer = 0;
    let discoveryTimer = 0;
    let scrollFramePending = false;
    let delayedScrollTimer = 0;

    const scrollToLatest = container => {
        if (scrollFramePending) return;
        scrollFramePending = true;
        requestAnimationFrame(() => {
            scrollFramePending = false;
            if (container !== observedContainer) return;
            container.scrollTop = container.scrollHeight;
            clearTimeout(delayedScrollTimer);
            delayedScrollTimer = setTimeout(() => {
                if (container === observedContainer) {
                    container.scrollTop = container.scrollHeight;
                }
            }, 80);
        });
    };

    const apply = () => {
        const root = document.head || document.documentElement;
        if (root) {
            let style = document.getElementById(styleId);
            if (!style) {
                style = document.createElement('style');
                style.id = styleId;
                root.appendChild(style);
            }
            if (style.textContent !== css) {
                style.textContent = css;
            }
        }

        const container = document.querySelector(config.messageContainer);
        if (container === observedContainer) {
            if (container) scrollToLatest(container);
            return;
        }

        containerObserver?.disconnect();
        containerObserver = null;
        observedContainer = container;
        if (!container) return;

        containerObserver = new MutationObserver(records => {
            if (records.some(record => record.addedNodes.length > 0)) {
                scrollToLatest(container);
            }
        });
        containerObserver.observe(container, { childList: true, subtree: true });
        scrollToLatest(container);
        if (discoveryTimer) {
            clearInterval(discoveryTimer);
            discoveryTimer = 0;
            reconciliationTimer = setInterval(apply, 10000);
        }
    };

    if (typeof window.__transparentChatFilterTwitchScrollFix === 'function') {
        window.__transparentChatFilterTwitchScrollFix();
        return;
    }

    window.__transparentChatFilterTwitchScrollFix = apply;
    discoveryTimer = setInterval(apply, 500);
    apply();
})();
