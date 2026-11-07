(() => {
    if (window.__transparentChatFilterTwitchCapture) return;
    window.__transparentChatFilterTwitchCapture = true;

    const config = window.__tcfTwitchDomConfig;
    if (!config) {
        window.chrome?.webview?.postMessage({
            kind: 'capture-error',
            message: 'A configuração dos seletores do chat não foi carregada.'
        });
        return;
    }

    const capturedLines = new WeakMap();
    const pendingLines = new Set();
    let container = null;
    let containerObserver = null;
    let discoveryTimer = 0;
    let reconciliationTimer = 0;
    let animationFramePending = false;

    const postMessage = message => window.chrome?.webview?.postMessage(message);
    const getImageUrl = image =>
        image.currentSrc || image.src || image.getAttribute('data-src') ||
        image.getAttribute('src') || '';

    const serializeBody = body => {
        let text = '';
        const emotes = [];
        const appendNode = node => {
            if (node.nodeType === Node.TEXT_NODE) {
                text += node.nodeValue || '';
                return;
            }
            if (node.nodeType !== Node.ELEMENT_NODE) return;

            const element = node;
            if (element.tagName.toLowerCase() === 'img') {
                const imageUrl = getImageUrl(element);
                let emoteId = '';
                try {
                    const imageUri = new URL(imageUrl, document.baseURI);
                    if (/(^|\.)jtvnw\.net$/i.test(imageUri.hostname)) {
                        emoteId = imageUri.pathname.match(/\/emoticons\/v(?:1|2)\/(\d+)(?:\/|$)/i)?.[1] || '';
                    }
                } catch {
                    emoteId = '';
                }

                const emoteText = element.getAttribute('alt') ||
                    element.getAttribute('title') ||
                    element.getAttribute('aria-label') ||
                    '';
                if (!emoteText) return;
                const start = text.length;
                text += emoteText;
                if (emoteId) {
                    emotes.push({ id: emoteId, start, end: text.length - 1 });
                }
                return;
            }

            for (const child of element.childNodes) appendNode(child);
        };
        for (const child of body.childNodes) appendNode(child);

        const trimmedText = text.trim();
        const leadingTrim = text.length - text.trimStart().length;
        const end = leadingTrim + trimmedText.length;
        return {
            text: trimmedText,
            emotes: emotes
                .filter(emote => emote.start >= leadingTrim && emote.end < end)
                .map(emote => ({
                    id: emote.id,
                    start: emote.start - leadingTrim,
                    end: emote.end - leadingTrim
                }))
        };
    };

    const getBadgeSetId = image => {
        const metadataElements = [image, image.parentElement, image.parentElement?.parentElement]
            .filter(Boolean);
        const explicitSetId = metadataElements
            .map(element => element.getAttribute('data-badge-set-id') ||
                element.getAttribute('data-badge-set') ||
                element.getAttribute('data-badge-type'))
            .find(Boolean);
        if (explicitSetId) return explicitSetId.toLowerCase();

        const imageUrl = getImageUrl(image);
        let url = null;
        try {
            url = new URL(imageUrl, document.baseURI);
        } catch {
            url = null;
        }
        const semanticId = value =>
            value?.match(/(?:^|[^a-z])(moderator|vip|subscriber|broadcaster|founder)(?:$|[^a-z])/i)?.[1]
                .toLowerCase() || '';
        if (url) {
            const pathSetId = semanticId(url.pathname.split('/').filter(Boolean).join('/'));
            if (pathSetId) return pathSetId;
        }

        for (const element of metadataElements) {
            const value = [
                element.getAttribute('data-badge'),
                element.getAttribute('data-badge-id'),
                element.getAttribute('data-a-target'),
                element.getAttribute('data-test-selector')
            ].filter(Boolean).join(' ');
            const dataSetId = semanticId(value);
            if (dataSetId) return dataSetId;
        }

        const cdnId = url?.pathname.match(/\/badges\/v1\/([^/]+)/i)?.[1];
        return cdnId ? `cdn-${cdnId}` : 'twitch-badge';
    };

    const isTwitchBadgeUrl = imageUrl => {
        try {
            const host = new URL(imageUrl, document.baseURI).hostname;
            return /(^|\.)jtvnw\.net$/i.test(host) || /(^|\.)twitch\.tv$/i.test(host);
        } catch {
            return false;
        }
    };

    const processLine = line => {
        if (!container?.contains(line)) return;
        const user = line.querySelector(config.username);
        const body = line.querySelector(config.messageBody);
        if (!user || !body) return;

        const displayName = (user.textContent || '').trim();
        const serializedBody = serializeBody(body);
        const message = serializedBody.text;
        if (!displayName || !message) return;

        const precedingImages = Array.from(line.querySelectorAll('img'))
            .filter(image =>
                (user.compareDocumentPosition(image) & Node.DOCUMENT_POSITION_PRECEDING) !== 0);
        const sharedChatImage = line.querySelector(config.sharedChatImage);
        const isChannelProfileImage = image => {
            const imageUrl = getImageUrl(image);
            const imagePath = imageUrl.split(/[?#]/, 1)[0];
            const accessibleName = [
                image.alt,
                image.getAttribute('aria-label'),
                image.title,
                image.parentElement?.getAttribute('aria-label'),
                image.parentElement?.title
            ].filter(Boolean).join(' ').toLowerCase();
            return /\/(?:jtv_user_pictures|user-pictures|channel_images|profiles)\//i.test(imagePath) ||
                /shared chat|channel (?:profile|picture|avatar)|channel icon/i.test(accessibleName);
        };
        const channelImage = sharedChatImage || precedingImages.find(isChannelProfileImage);
        const channelImageUrl = channelImage ? getImageUrl(channelImage) : '';
        const badges = precedingImages
            .map(image => {
                const imageUrl = getImageUrl(image);
                if (!isTwitchBadgeUrl(imageUrl)) return null;
                const title = (image.alt || image.getAttribute('aria-label') ||
                    image.title || image.parentElement?.getAttribute('aria-label') ||
                    image.parentElement?.title || 'Twitch badge').trim();
                return { setId: getBadgeSetId(image), title, imageUrl };
            })
            .filter(badge => badge && badge.imageUrl && badge.imageUrl !== channelImageUrl);

        const signature = JSON.stringify({
            displayName,
            message,
            emotes: serializedBody.emotes,
            channelImageUrl,
            badges
        });
        if (capturedLines.get(line) === signature) return;

        const pathSegments = location.pathname.split('/').filter(Boolean);
        const channel = pathSegments[0] === 'popout' ? pathSegments[1] || '' : pathSegments[0] || '';
        postMessage({
            kind: 'message',
            displayName,
            message,
            emotes: serializedBody.emotes,
            channel,
            channelImageUrl,
            isModerator: badges.some(badge => badge.setId === 'moderator'),
            isVip: badges.some(badge => badge.setId === 'vip'),
            isSubscriber: badges.some(badge => badge.setId === 'subscriber'),
            badges
        });
        capturedLines.set(line, signature);
    };

    const collectLinesFromNode = node => {
        if (node.nodeType === Node.TEXT_NODE) {
            const line = node.parentElement?.closest(config.messageLine);
            if (line) pendingLines.add(line);
            return;
        }
        if (node.nodeType !== Node.ELEMENT_NODE) return;
        const element = node;
        if (element.matches(config.messageLine)) {
            pendingLines.add(element);
            return;
        }
        const parentLine = element.closest(config.messageLine);
        if (parentLine) {
            pendingLines.add(parentLine);
            return;
        }
        element.querySelectorAll(config.messageLine).forEach(line => pendingLines.add(line));
    };

    const flushPendingLines = () => {
        animationFramePending = false;
        for (const line of pendingLines) processLine(line);
        pendingLines.clear();
    };

    const queueAddedNodes = nodes => {
        for (const node of nodes) collectLinesFromNode(node);
        if (pendingLines.size > 0 && !animationFramePending) {
            animationFramePending = true;
            requestAnimationFrame(flushPendingLines);
        }
    };

    const attachToMessageContainer = () => {
        const nextContainer = document.querySelector(config.messageContainer);
        if (!nextContainer) return false;
        if (nextContainer === container) return true;

        containerObserver?.disconnect();
        container = nextContainer;
        containerObserver = new MutationObserver(records => {
            for (const record of records) queueAddedNodes(record.addedNodes);
        });
        containerObserver.observe(container, { childList: true, subtree: true });
        container.querySelectorAll(config.messageLine).forEach(line => pendingLines.add(line));
        if (!animationFramePending) {
            animationFramePending = true;
            requestAnimationFrame(flushPendingLines);
        }
        postMessage({ kind: 'capture-ready' });
        return true;
    };

    const reconcile = () => {
        if (!attachToMessageContainer()) {
            if (containerObserver) {
                containerObserver.disconnect();
                containerObserver = null;
                container = null;
            }
            if (!discoveryTimer) discoveryTimer = setInterval(discoverContainer, 500);
            return;
        }
        container.querySelectorAll(config.messageLine).forEach(processLine);
    };

    function discoverContainer() {
        if (!attachToMessageContainer()) return;
        clearInterval(discoveryTimer);
        discoveryTimer = 0;
        reconciliationTimer = setInterval(reconcile, 10000);
    }

    discoveryTimer = setInterval(discoverContainer, 500);
    discoverContainer();
})();
