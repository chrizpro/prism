# AI assistant

The assistant lets you describe what you want in plain language and have an AI model do the modeling for you: "make a 60 × 40 mm plate with four M5 holes 6 mm from the corners", "fillet the top edges by 2 mm", "why did that boolean fail?". It works the open document through the same tools as [scripts](/api/scripting), so everything it does appears in the design history and can be undone.

## Open it

Choose **✦ Assistant** in the header. Type a request and choose **Send**; **Stop** interrupts it. The panel shows the assistant's replies and each tool it uses. **⟲** starts a new conversation, and **⚙** opens its settings.

## Choose a provider

| Provider | What you need | Cost |
| --- | --- | --- |
| **Codex (your login)** | Install OpenAI's [Codex CLI](https://github.com/openai/codex) and run `codex login` once | Included in your Codex plan |
| **Claude (API key)** | An [Anthropic API key](https://console.anthropic.com/) | Billed to your Anthropic API account |
| **OpenAI (API key)** | An [OpenAI API key](https://platform.openai.com/) | Billed to your OpenAI API account |

Pick the provider in the panel or in **Settings › AI Assistant**. You can also enter a model name; leave it blank for the provider's default.

Claude is available with an API key only. Prism does not offer Claude subscription login.

<GuideImage image="assistant-modeling-panel" />

## Settings

- **Remember the key on this computer** saves your API keys so you don't need to enter them again. They are stored in plain text in `%LOCALAPPDATA%\Prism\assistant.json`. Without it, keys last until Prism closes. Prism also picks up `ANTHROPIC_API_KEY` and `OPENAI_API_KEY` from your environment.
- **Let the assistant open, save and export files** gives it the file tools. It is off by default.

## Good to know

- The assistant checks its work by reading the model and taking screenshots, so it can see what it built.
- The assistant is told to ask before anything large or destructive. Use **Undo** if you don't like a result.
- Your requests and what the assistant reads from the model are sent to the provider you chose. See [Privacy](/reference/privacy).
