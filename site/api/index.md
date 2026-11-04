# Scripting and API

Everything you can click in Prism can also be driven from code. The same automation API serves three kinds of client:

| Client | Use it for | Start here |
| --- | --- | --- |
| **Python with `cadkit`** | Scripts that build parts, batch jobs, parametric tools with their own dialogs | [Python scripting](/api/scripting) |
| **Any language over HTTP** | Integrations from C#, JavaScript, test harnesses and pipelines | [HTTP protocol](/api/http) |
| **AI assistants over MCP** | Letting an assistant inspect, model, simulate and take screenshots | [MCP bridge](/api/mcp) |

```text
Python script ─┐
HTTP client ───┼──>  127.0.0.1 + token  ──>  Prism (AutomationServer → AutomationApi)
MCP bridge ────┘        (localhost only)          same commands as the mouse
```

Tools run on the application's UI thread through the same session and command code as interactive work. That means every change appears in the viewport, lands in the design history and can be undone.

## Reference

- [Python reference](/api/python): every `cadkit` function.
- [Automation tools](/api/automation): the tools behind `cad.call()`, `POST /call` and `call_api`, with their arguments.
- [Command reference](/api/commands): every modeling command, with its selection slots and parameters.
- [Examples](/api/examples): the scripts that ship with Prism.

## Conventions

- Units are millimetres and degrees. Z is up.
- Face and edge numbers are **1-based**, matching the labels in the app. Freeform cage indices (`form_info`, `form_op`) are **0-based**.
- Objects can be named by their name (`"Box 1"`) or an id prefix.
- Slot and parameter labels match by partial, case-insensitive text: `k_factor` finds **K-factor**.
- Commands fill empty selection slots from the current selection.
- Numbers and booleans sent as text (`"5"`, `"true"`) are accepted.

## Security

The automation endpoint binds to `127.0.0.1` only and requires a random token that changes every time Prism starts. Requests carrying a browser `Origin` header are refused, so web pages cannot reach it. Scripts run with your own user permissions, like any program you start.

Set `PRISM_AUTOMATION=0` before starting Prism to turn the endpoint off.
