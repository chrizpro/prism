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
const config = dotnetRuntime.getConfig();

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
