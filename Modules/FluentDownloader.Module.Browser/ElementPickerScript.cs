namespace FluentDownloader.Module.Browser;

/// <summary>注入页面的“点选模式”脚本：悬停高亮、点击抓取资源地址、Esc 退出。再执行一次即关闭。</summary>
public static class ElementPickerScript
{
    public const string Source = """
        (() => {
            if (window.__fdlPicker) {
                window.__fdlPicker.stop();
                window.__fdlPicker = null;
                return false;
            }

            const STYLE_ID = '__fdl_style';
            let style = document.getElementById(STYLE_ID);
            if (!style) {
                style = document.createElement('style');
                style.id = STYLE_ID;
                style.textContent = '.__fdl_hl{outline:2px solid #0F6CBD !important;outline-offset:-2px !important;cursor:crosshair !important;background-color:rgba(15,108,189,.14) !important;}';
                document.documentElement.appendChild(style);
            }

            let last = null;
            const clear = () => { if (last) { last.classList.remove('__fdl_hl'); last = null; } };
            const onMove = (e) => {
                clear();
                last = e.target;
                if (last && last.classList) last.classList.add('__fdl_hl');
            };

            const pickAttribute = (el) => {
                const attrs = ['href', 'src', 'data-src', 'data-url', 'data-video', 'data-mp4', 'data-original'];
                for (const a of attrs) {
                    const v = el.getAttribute && el.getAttribute(a);
                    if (v && v.trim()) return v.trim();
                }
                return null;
            };

            const extract = (el) => {
                let u = pickAttribute(el);
                if (!u) {
                    const media = el.querySelector && el.querySelector('source[src],video[src],img[src]');
                    if (media) u = pickAttribute(media);
                }
                if (!u) {
                    const anchor = el.closest && el.closest('a[href]');
                    if (anchor) u = anchor.getAttribute('href');
                }
                if (!u) {
                    const bg = el.style && el.style.backgroundImage;
                    if (bg) {
                        const m = bg.match(/url\(["']?([^"')]+)["']?\)/);
                        if (m) u = m[1];
                    }
                }
                if (!u) return null;
                try { return new URL(u, location.href).href; } catch (_) { return null; }
            };

            const onClick = (e) => {
                const url = extract(e.target);
                if (!url) return; // 没有资源地址的点击放行，页面照常交互
                e.preventDefault();
                e.stopPropagation();
                clear();
                window.chrome.webview.postMessage({
                    type: 'fdl-pick',
                    url: url,
                    tag: e.target.tagName || '',
                    text: (e.target.textContent || '').trim().slice(0, 60),
                });
            };

            const onKey = (e) => {
                if (e.key === 'Escape') {
                    window.__fdlPicker.stop();
                    window.__fdlPicker = null;
                }
            };

            document.addEventListener('mousemove', onMove, true);
            document.addEventListener('click', onClick, true);
            document.addEventListener('keydown', onKey, true);

            window.__fdlPicker = {
                stop() {
                    document.removeEventListener('mousemove', onMove, true);
                    document.removeEventListener('click', onClick, true);
                    document.removeEventListener('keydown', onKey, true);
                    clear();
                    if (style) style.remove();
                },
            };
            return true;
        })()
        """;
}
