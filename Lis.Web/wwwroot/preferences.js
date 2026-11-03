// LIS Web User Preferences & MDB Theme Helper
window.lisApplyPreferences = function (theme, density, accentHex, language) {
    try {
        if (!theme) theme = 'dark';
        if (!language) language = 'ar';
        document.documentElement.setAttribute('data-mdb-theme', theme);
        document.documentElement.setAttribute('data-lis-theme', theme);
        document.documentElement.setAttribute('data-lis-lang', language);
        document.documentElement.setAttribute('lang', language);
        
        if (density) {
            document.documentElement.setAttribute('data-lis-density', density);
        }

        if (accentHex) {
            document.documentElement.style.setProperty('--accent', accentHex);
            document.documentElement.style.setProperty('--mdb-primary', accentHex);
        }

        // Apply theme class to body and documentElement
        if (theme === 'light') {
            document.body?.classList.add('lis-light-theme');
            document.body?.classList.remove('lis-dark-theme');
            document.documentElement.classList.add('lis-light-theme');
            document.documentElement.classList.remove('lis-dark-theme');
        } else {
            document.body?.classList.add('lis-dark-theme');
            document.body?.classList.remove('lis-light-theme');
            document.documentElement.classList.add('lis-dark-theme');
            document.documentElement.classList.remove('lis-light-theme');
        }
    } catch (e) {
        console.error('Error applying user preferences:', e);
    }
};

window.lisPlayScanSound = function () {
    try {
        const audioCtx = new (window.AudioContext || window.webkitAudioContext)();
        const osc = audioCtx.createOscillator();
        const gain = audioCtx.createGain();
        osc.connect(gain);
        gain.connect(audioCtx.destination);
        osc.type = 'sine';
        osc.frequency.setValueAtTime(880, audioCtx.currentTime); // 880 Hz
        gain.gain.setValueAtTime(0.1, audioCtx.currentTime);
        osc.start();
        osc.stop(audioCtx.currentTime + 0.12);
    } catch (e) { }
};

// Initial run from localStorage or URL parameter before Blazor hydrates to prevent flash of wrong theme
(function () {
    try {
        const urlParams = new URLSearchParams(window.location.search);
        const queryTheme = urlParams.get('theme');
        const queryLang = urlParams.get('lang');

        if (queryLang === 'ar' || queryLang === 'en') {
            document.documentElement.setAttribute('data-lis-lang', queryLang);
            document.documentElement.setAttribute('lang', queryLang);
        }

        if (queryTheme === 'light' || queryTheme === 'dark') {
            document.documentElement.setAttribute('data-mdb-theme', queryTheme);
            document.documentElement.setAttribute('data-lis-theme', queryTheme);
            if (queryTheme === 'light') {
                document.documentElement.classList.add('lis-light-theme');
            } else {
                document.documentElement.classList.add('lis-dark-theme');
            }
            return;
        }

        const raw = localStorage.getItem('lis_user_preferences');
        if (raw) {
            const p = JSON.parse(raw);
            if (p.Language) {
                document.documentElement.setAttribute('data-lis-lang', p.Language);
                document.documentElement.setAttribute('lang', p.Language);
            }
            if (p.Theme) {
                document.documentElement.setAttribute('data-mdb-theme', p.Theme);
                document.documentElement.setAttribute('data-lis-theme', p.Theme);
                if (p.Theme === 'light') {
                    document.documentElement.classList.add('lis-light-theme');
                } else {
                    document.documentElement.classList.add('lis-dark-theme');
                }
            }
            if (p.Density) document.documentElement.setAttribute('data-lis-density', p.Density);
        }
    } catch (e) { }
})();

