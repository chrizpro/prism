# AI assistant

The assistant panel (**✦ Assistant** in the header) lets a language model work the open document through the same automation tools as the [MCP bridge](../../MCP.md). Everything it does is an ordinary, undoable step. Code: `Prism/Automation/AssistantService.cs`, `Prism/ViewModels/MainViewModel.Assistant.cs`, `Prism/Views/AssistantPanel.axaml`, and the AI Assistant page of `Prism/Views/SettingsWindow.axaml`.

## Providers

| Provider (as listed) | How it connects | Needs |
| --- | --- | --- |
| Codex (your login) | Runs the user's installed `codex` CLI with the Prism MCP bridge; the assistant tool is disabled for it | `codex login` once; marked Experimental |
| Claude (API key) | Anthropic Messages API | The user's Anthropic API key |
| OpenAI/Codex (API key) | OpenAI Chat Completions API — `gpt-*` and `codex-*` models both work | The user's OpenAI API key |

**Owner decision (2026-11-04):** Claude is offered with an API key only. The "Claude (your login)" option, which ran the user's Claude Code CLI, was removed, because Anthropic does not allow third-party products to offer Claude subscription login without approval. Codex login remains.

`AiProvider` values are stored in `%LOCALAPPDATA%\Prism\assistant.json` as numbers: `CodexCli = 1`, `Anthropic = 2`, `OpenAi = 3`. The removed login was `0`; a stored `0` (or any unknown value) loads as `Anthropic`, so those users keep Claude and are asked for a key. New settings default to Codex. Both provider dropdowns list the providers in enum order, so dropdown position = value − 1.

## Models and effort (approved 2026-02-09)

The model field is a **populated picker**, not free text. Rows can show "id   (label)" — only the id is stored and sent to the provider. The list reloads when the panel or Settings opens, when the provider or API key changes, and on the ↻ button. A saved id that no longer appears in the catalog is kept and still sent.

After each reply a faint **usage** line reports the tokens the provider billed: `usage: N in · M out` (plus `· C cached` when reported). Codex reads `turn.completed` usage, Claude `input_tokens`/`output_tokens` (with cache tokens), OpenAI `prompt_tokens`/`completion_tokens` — each summed across the tool-call turns of one send.

- Codex: `codex debug models` (falling back to `~/.codex/models_cache.json`); the empty first row is the CLI's own default.
- Claude: `GET /v1/models` filtered to `claude*` ids.
- OpenAI: `GET /v1/models` filtered to tool-using chat ids (`IsChatModel`): audio, image, embedding, transcription, moderation and search models are excluded.

When discovery fails the picker shows the saved id and a note ("Model list unavailable — the saved model is used").

**Effort** is a second dropdown shown only when the selected model reports levels. Row 0 is `Default` (no effort sent). Codex uses each model's `supported_reasoning_levels` and passes `-c model_reasoning_effort=<effort>`; Claude sends `output_config.effort` (the known Messages set low/medium/high is offered because the model catalog lists no levels); OpenAI sends `reasoning_effort` on reasoning models (`IsReasoningModel`: `o*`, `gpt-5*`, `codex*`, `*reasoning*` — plain `gpt-4o`/`gpt-4.1`/`chatgpt-*` get no effort row). Values are stored per provider as `CliEffort`/`AnthropicEffort`/`OpenAiEffort`.

## Conversation context

A conversation keeps its earlier turns, so follow-ups like "scale it 2×" know what "it" is.

- Claude and OpenAI resend the message history with each turn.
- Codex runs `codex exec resume <thread>` so the CLI session itself keeps earlier turns and tool calls. If the stored session can no longer be resumed (cleaned up, a different Codex version), the assistant retries once as a fresh session and includes the conversation so far as a transcript recap.
- The **⟲ New conversation** button clears history, the Codex thread and any entry-point context. Switching provider also starts over.

**Errors and remedies.** When Codex fails with a sign-in error the header shows **Sign in to Codex** (runs `codex login` in a terminal; resend afterwards). When an API provider rejects the key (401) the header shows **Check the API key**, which opens the assistant's ⚙ settings strip. Other failures appear as red error rows; a lost Codex session produces a note that a new session started with the transcript. (Owner-requested remedy parity 2026-11-06.)

## Keys and settings

Keys entered in the panel are kept in memory and saved only when **Remember the key on this computer** is on, in plain text in `assistant.json`. Without a saved file, keys are read from `ANTHROPIC_API_KEY` and `OPENAI_API_KEY`. `PRISM_ASSISTANT_FILE` overrides the settings path (tests use it).

**Let the assistant open, save and export files** is off by default. When off, the assistant cannot:

- use the `file`, `mesh_export`, `mesh_import` or `drawing_export` tools,
- run scripts at all (`run_script` — the Codex bridge connects to a restricted endpoint whose `/script` is blocked and which does not advertise `run_script`; API providers are refused per call),
- run or save the script editor (`script_editor` `run`/`save`), save renders (`render_control save`) or import a backdrop (`render_setup set_background`).

The check is `AutomationApi.Restricted(name, args)`, enforced inside the in-app `CallTool` and on the restricted endpoint token (`assistant-automation.json` next to `automation.json`). Everything else — modeling, inspection, screenshots, `script_editor` reads and `set`/`insert` — stays available.

## Images (approved 2026-02-09)

The 📎 button attaches image files (PNG, JPEG, WebP, GIF, BMP; ≤ 10 MB each, up to 4 per message) or a capture of the current 3D view; Ctrl+V pastes a picture. Thumbnails show under the input and in the sent message. Claude receives `image` content blocks, OpenAI `image_url` parts, and Codex gets temp files passed with `--image`. Tool-produced screenshots are unchanged (Claude image blocks; OpenAI still gets a text note).

## Script editor (approved 2026-02-09)

The Script panel has **✦ AI**, which opens the assistant, sets a "write a script into the editor" context that stays with the conversation until a new one is started, and sends a greeting telling it a script request is coming (when the assistant is busy it stages the message instead), and **⟲**, which rolls back the assistant's last rewrite. The `script_editor` tool replaces the **whole** script (`set`) or inserts before a line (`insert`); each call snapshots the previous text (up to 10) for `rollback`/⟲. Rewrites mark the editor dirty and never save to disk. `run`/`save` need file access (above). Owner decision: whole-file replacement is fine because scripts are single-file and short.

## Render background (approved 2026-02-09)

The Render command has **Generate background with AI…** next to the backdrop controls. It opens the assistant with a "generate a render background" context that stays until a new conversation, and stages a prompt asking Codex to write a PNG into `%LOCALAPPDATA%\Prism\generated` with `run_script`, then apply it with `render_setup action=set_background` (validated through `SurfaceImage`, 16 MB limit). Without file access it explains how to enable the permission instead. Codex is orchestrating procedural PNG generation here — it is not a general text-to-image provider.

The `assistant` automation tool drives the panel for tests: `provider` takes the dropdown position (0 Codex, 1 Claude API key, 2 OpenAI API key).

## MCP client setup

The **AI Assistant** page of Settings ends with **MCP — use Prism from an external agent**. The read-only box shows the ready-to-paste config for both clients; **Copy for Claude** / **Copy for Codex** put it on the clipboard. **Set up Claude Desktop** / **Set up Codex** (owner-requested 2026-11-06) write the `prism` entry into the client's own config file directly — `%APPDATA%\Claude\claude_desktop_config.json` on Windows (`~/Library/Application Support/Claude/` on macOS, `~/.config/Claude/` on Linux) or `~/.codex/config.toml` — merging with what is already there and keeping the previous file as `.bak`. A note reports where it wrote or why it could not (e.g. malformed JSON → use Copy). The entry points at the `Prism.Mcp` bridge discovered next to the app; a missing bridge reports the `dotnet build Prism.Mcp -c Release` remedy.

## Privacy

What is sent to each provider is described on the public [privacy page](../../site/reference/privacy.md).

## Verification

- [Prism.Tests/Unit/Assistant/AssistantCatalogTests.cs](../../../Prism.Tests/Unit/Assistant/AssistantCatalogTests.cs) — model/effort catalog parsing (Codex, Anthropic, OpenAI filters), `Restricted` gating incl. `batch` nesting (`AST-U001`–`AST-U007`).
- [Prism.Tests/Unit/Assistant/AssistantCliSetupTests.cs](../../../Prism.Tests/Unit/Assistant/AssistantCliSetupTests.cs) — `codex exec`/`exec resume` argument building incl. the `-c sandbox_mode` override (`AST-U008`–`AST-U010`), Claude/Codex config merge (`AST-U011`–`AST-U012`), model-id and usage helpers (`AST-U013`–`AST-U014`).
- [Prism.Tests/Headless/Assistant/AssistantToolTests.cs](../../../Prism.Tests/Headless/Assistant/AssistantToolTests.cs) — `settings` whitelist (list/get/set/reset, no secrets), restricted endpoint behavior, `script_editor` replace/rollback/dirty, `render_setup` (`AST-H001`–`AST-H008`).
- `Prism.Tests/Integration` — tracked via the parent feature issue; provider calls always need external accounts.
