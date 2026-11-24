import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

// The loading page's bar: the download takes it to 80 %, starting .NET and the geometry kernel the rest of the way.
// Avalonia removes the page once the app has drawn its first frame.
const fill = document.getElementById('splash-fill');
const status = document.getElementById('splash-status');
function stage(text, fraction) {
    if (status) status.textContent = text;
    if (fill) fill.style.width = Math.round(Math.max(2, Math.min(100, fraction * 100))) + '%';
}

// Before anything downloads: browsers without WebGL 2 cannot draw Prism at all, and phones get a choice first (v0.5, owner request 2026-11-23).
// A phone is a touch screen whose smaller side is under 600 CSS pixels; tablets and computers go straight on. See docs/interface/browser/README.md.
const notice = document.getElementById('splash-notice');
function showNotice(title, text, canContinue) {
    document.getElementById('notice-title').textContent = title;
    document.getElementById('notice-text').textContent = text;
    document.getElementById('notice-continue').hidden = !canContinue;
    notice.hidden = false;
    if (status) status.hidden = true;
    if (fill) fill.parentElement.hidden = true;
}
function hideNotice() {
    notice.hidden = true;
    if (status) status.hidden = false;
    if (fill) fill.parentElement.hidden = false;
}
const hasWebGl2 = (() => { try { return !!document.createElement('canvas').getContext('webgl2'); } catch { return false; } })();
if (!hasWebGl2) {
    showNotice('This browser cannot run Prism',
        'Prism draws its workspace and 3D view with WebGL 2, which this browser or device does not provide. Try a current Chrome, Edge, Firefox or Safari, or install the desktop app.',
        false);
    await new Promise(() => { });
}
const smallSide = Math.min(screen.width, screen.height);
const phone = smallSide < 600 && matchMedia('(pointer: coarse)').matches;
const lowMemory = typeof navigator.deviceMemory === 'number' && navigator.deviceMemory < 4;
const saveData = navigator.connection?.saveData === true;
let remembered = false;
try { remembered = localStorage.getItem('prism-continue-on-this-device') === 'yes'; } catch { }
if ((phone || lowMemory || saveData) && !remembered) {
    const parts = [];
    if (phone) parts.push('Prism is a full CAD program made for a mouse or trackpad and a large screen; most tools are hard to use on a phone.');
    if (lowMemory || phone) parts.push('Phones and devices with little memory may run it slowly or close it while you work.');
    parts.push(`It downloads about 20 MB${saveData ? ', and your browser asked to save data' : ''}. Later visits open from the browser's cache.`);
    showNotice(phone ? 'Prism works best on a computer' : 'Prism may run slowly on this device', parts.join(' '), true);
    await new Promise(resolve => document.getElementById('notice-continue').addEventListener('click', () => {
        try { localStorage.setItem('prism-continue-on-this-device', 'yes'); } catch { }
        hideNotice();
        resolve();
    }, { once: true }));
}

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .withModuleConfig({
        onDownloadResourceProgress: (loaded, total) => {
            if (total > 0) stage(loaded < total ? `Downloading Prism… ${Math.round(loaded / total * 100)} %` : 'Starting Prism…', 0.8 * loaded / total);
        },
    })
    .create();

stage('Starting the geometry kernel…', 0.9);
console.log(`Prism web start-up: downloads finished and .NET created at ${Math.round(performance.now())} ms after the page started loading`);
const config = dotnetRuntime.getConfig();

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
