# Privacy

Prism is a desktop application. Your models stay on your computer unless you choose a feature that sends them somewhere. Prism has no accounts, no telemetry and no analytics. On Windows, macOS and Linux it checks once at startup whether a newer release exists, and you can turn that off.

## What can leave your computer

| Feature | What is sent | Where | When |
| --- | --- | --- | --- |
| **AI assistant** with an API key | Your messages and whatever the assistant reads from Prism to answer you, which can include object names, sizes, geometry details and screenshots | Anthropic (Claude) or OpenAI, whichever you choose | Only while you use the assistant |
| **AI assistant** with your Codex login | The same, through the Codex program you installed | OpenAI, under Codex's terms | Only while you use the assistant |
| **AR sharing** | The exported GLB and USDZ files of the visible model | Any device on your local network that opens the address Prism shows | Until you choose **Stop Sharing** |
| **Update check** | A request for the list of Prism releases, which includes your IP address and the name `Prism-UpdateCheck`. No document, setting or system information is sent | GitHub (`api.github.com`) | Once at startup, and when you choose **Help → Check for updates…**. Off with **Turn off startup update checks** in the update window, or in **Settings** |
| **Model gallery** | Requests for the gallery list, its preview pictures and any model you choose to download, which include your IP address. No document or setting is sent | GitHub (`raw.githubusercontent.com`) | When the home screen opens, on **Refresh**, and when you choose **Download & open** |
| **Help and documentation links** | A normal web page request | This website, on GitHub Pages | When you open help |

Each AI provider handles what you send under its own privacy policy and terms. Prism does not send anything to the Prism project. The update check only reads the public release list; nothing is installed without you downloading and running the installer.

### AI assistant keys

API keys are kept in memory unless you choose to remember them. Remembered keys are saved in plain text in `%LOCALAPPDATA%\Prism\assistant.json`, readable by anyone who can read your user files. Prism also reads `ANTHROPIC_API_KEY` and `OPENAI_API_KEY` from your environment.

### AR sharing

While sharing is on, Prism serves the exported model to your local network so a phone can open it. Anyone on the same network who knows the address can download the model. The page shown on the phone loads Google's model-viewer script from Google's servers, so your phone contacts Google while viewing.

## What stays local

- **Automation and scripts.** The automation endpoint used by scripts and the MCP bridge listens on `127.0.0.1` only, requires a random token that changes each time Prism starts, and refuses requests from web browsers. Set `PRISM_AUTOMATION=0` to turn it off. See [Scripting and API](/api/#security).
- **Optional tools.** CalculiX, ffmpeg, Python and slicers run on your computer as separate programs.
- **Documents and settings.** Models, preferences and caches are stored in your workspace folder, `Documents\Prism` and `%LOCALAPPDATA%\Prism`.

## The web version

The web version runs entirely in your browser. Your models are kept in the browser tab's memory and are never uploaded: opening and saving go through your browser's own file dialogs, and nothing is kept after you close the tab unless you save it.

- **Loading it** downloads the app (about 20 MB) from GitHub Pages, which, like any website host, receives your IP address and standard request information. Your browser keeps the files in its normal cache so later visits are faster.
- **The model gallery** fetches its list, pictures and models from GitHub, as on the desktop.
- **On phones and low-memory devices**, if you choose **Continue anyway**, that choice is remembered in your browser's local storage (`prism-continue-on-this-device`) so the notice is not shown again. Clearing the site's data removes it.
- There are no update checks, no AI assistant, no scripts and no automation endpoint in the web version, and it sets no cookies.

## The websites

The website and this documentation are static pages on GitHub Pages. They set no cookies and include no analytics or advertising. Fonts are served from the sites themselves. GitHub, as the host, receives standard request information such as your IP address, under the [GitHub Privacy Statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement).
