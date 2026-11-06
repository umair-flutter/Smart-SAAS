// Small browser helpers called from Blazor through JS interop.
window.smartSchool = (function () {
    let lastActivity = Date.now();
    ["mousemove", "keydown", "click", "scroll", "touchstart"].forEach(evt =>
        window.addEventListener(evt, () => { lastActivity = Date.now(); }, { passive: true }));

    return {
        storageGet: key => {
            try { return localStorage.getItem(key); } catch { return null; }
        },
        storageSet: (key, value) => {
            try { localStorage.setItem(key, value); } catch { /* storage unavailable */ }
        },
        storageRemove: key => {
            try { localStorage.removeItem(key); } catch { /* storage unavailable */ }
        },
        // Milliseconds since the user last interacted with the page.
        idleMs: () => Date.now() - lastActivity,
        // Saves bytes received from .NET as a file download.
        saveFile: (fileName, contentType, bytes) => {
            const blob = new Blob([bytes], { type: contentType });
            const url = URL.createObjectURL(blob);
            const a = document.createElement("a");
            a.href = url;
            a.download = fileName;
            document.body.appendChild(a);
            a.click();
            a.remove();
            setTimeout(() => URL.revokeObjectURL(url), 10000);
        },
        setDocumentLanguage: (lang, rtl) => {
            document.documentElement.lang = lang;
            document.documentElement.dir = rtl ? "rtl" : "ltr";
            // Tabler ships a mirrored stylesheet for right-to-left languages.
            const link = document.getElementById("tabler-css");
            if (link) link.href = link.href.replace(/tabler(.rtl)?.min.css/, rtl ? "tabler.rtl.min.css" : "tabler.min.css");
        }
    };
})();
