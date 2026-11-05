# AI assistant

The assistant panel (**✦ Assistant** in the header) lets a language model work the open document through the same automation tools as the [MCP bridge](../../MCP.md). Everything it does is an ordinary, undoable step. Code: `Prism/Automation/AssistantService.cs`, `Prism/ViewModels/MainViewModel.Assistant.cs`, `Prism/Views/AssistantPanel.axaml`, and the AI Assistant page of `Prism/Views/SettingsWindow.axaml`.

## Providers

| Provider (as listed) | How it connects | Needs |
| --- | --- | --- |
| Codex (your login) | Runs the user's installed `codex` CLI with the Prism MCP bridge; the assistant and, unless allowed, file tools are disabled for it | `codex login` once; marked Experimental |
| Claude (API key) | Anthropic Messages API | The user's Anthropic API key |
| OpenAI (API key) | OpenAI Chat Completions API | The user's OpenAI API key |

**Owner decision (2026-11-04):** Claude is offered with an API key only. The "Claude (your login)" option, which ran the user's Claude Code CLI, was removed, because Anthropic does not allow third-party products to offer Claude subscription login without approval. Codex login remains.

`AiProvider` values are stored in `%LOCALAPPDATA%\Prism\assistant.json` as numbers: `CodexCli = 1`, `Anthropic = 2`, `OpenAi = 3`. The removed login was `0`; a stored `0` (or any unknown value) loads as `Anthropic`, so those users keep Claude and are asked for a key. New settings default to Codex. Both provider dropdowns list the providers in enum order, so dropdown position = value − 1.

## Keys and settings

Keys entered in the panel are kept in memory and saved only when **Remember the key on this computer** is on, in plain text in `assistant.json`. Without a saved file, keys are read from `ANTHROPIC_API_KEY` and `OPENAI_API_KEY`. **Let the assistant open, save and export files** lets the model use the `file` tool (open, save, import, export); it is off by default.

The `assistant` automation tool drives the panel for tests: `provider` takes the dropdown position (0 Codex, 1 Claude API key, 2 OpenAI API key).

## Privacy

What is sent to each provider is described on the public [privacy page](../../site/reference/privacy.md).

## Verification

No maintained tests cover the assistant yet. Provider calls need external accounts, so coverage would start with settings migration and provider-to-dropdown mapping in Unit tests.
