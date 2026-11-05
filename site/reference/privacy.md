# Privacy

Prism is a desktop application. Your models stay on your computer unless you choose a feature that sends them somewhere. Prism has no accounts, no telemetry, no analytics and no automatic update checks.

## What can leave your computer

| Feature | What is sent | Where | When |
| --- | --- | --- | --- |
| **AI assistant** with an API key | Your messages and whatever the assistant reads from Prism to answer you, which can include object names, sizes, geometry details and screenshots | Anthropic (Claude) or OpenAI, whichever you choose | Only while you use the assistant |
| **AI assistant** with your Codex login | The same, through the Codex program you installed | OpenAI, under Codex's terms | Only while you use the assistant |
| **AR sharing** | The exported GLB and USDZ files of the visible model | Any device on your local network that opens the address Prism shows | Until you choose **Stop Sharing** |
| **Help and documentation links** | A normal web page request | This website, on GitHub Pages | When you open help |

Each AI provider handles what you send under its own privacy policy and terms. Prism does not send anything to the Prism project.

### AI assistant keys

API keys are kept in memory unless you choose to remember them. Remembered keys are saved in plain text in `%LOCALAPPDATA%\Prism\assistant.json`, readable by anyone who can read your user files. Prism also reads `ANTHROPIC_API_KEY` and `OPENAI_API_KEY` from your environment.

### AR sharing

While sharing is on, Prism serves the exported model to your local network so a phone can open it. Anyone on the same network who knows the address can download the model. The page shown on the phone loads Google's model-viewer script from Google's servers, so your phone contacts Google while viewing.

## What stays local

- **Automation and scripts.** The automation endpoint used by scripts and the MCP bridge listens on `127.0.0.1` only, requires a random token that changes each time Prism starts, and refuses requests from web browsers. Set `PRISM_AUTOMATION=0` to turn it off. See [Scripting and API](/api/#security).
- **Optional tools.** CalculiX, ffmpeg, Python and slicers run on your computer as separate programs.
- **Documents and settings.** Models, preferences and caches are stored in your workspace folder, `Documents\Prism` and `%LOCALAPPDATA%\Prism`.

## The websites

The website and this documentation are static pages on GitHub Pages. They set no cookies and include no analytics or advertising. Fonts are served from the sites themselves. GitHub, as the host, receives standard request information such as your IP address, under the [GitHub Privacy Statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement).
