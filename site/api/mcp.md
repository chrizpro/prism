# MCP bridge

`Prism.Mcp` lets an AI assistant drive Prism through the [Model Context Protocol](https://modelcontextprotocol.io). It speaks MCP over stdio and forwards calls to the app's [HTTP endpoint](/api/http).

```text
AI assistant  <── stdio MCP ──>  Prism.Mcp  <── HTTP 127.0.0.1 + token ──>  Prism
```

## Set up

The installer includes the bridge next to the desktop app. To build it from source:

```powershell
dotnet build Prism.Mcp -c Release
```

Then register it with your MCP client. In a project `.mcp.json`:

```json
{
  "mcpServers": {
    "prism": {
      "command": "dotnet",
      "args": ["Prism.Mcp/bin/Release/net10.0/Prism.Mcp.dll"]
    }
  }
}
```

For an installed copy, point `command` at the installed `Prism.Mcp` executable instead.

The bridge launches Prism on the first call if it is not running (without the home screen). Set `PRISM_EXE` to choose which executable it starts.

## What the assistant sees

| Tool | Purpose |
| --- | --- |
| `app_status`, `app_launch`, `app_close` | Check, start or stop the app |
| `run_script` | Run Python with `cadkit` against the live document; failures roll back unless `atomic: false` |
| `describe_api` | List the [automation tools](/api/automation), search them, or get full schemas by name |
| `call_api` | Call one automation tool by name |

Keeping the list short saves the assistant's context. To advertise every automation tool individually to older clients, set `PRISM_MCP_FULL_TOOLS=1` on the bridge; direct calls by name keep working either way.

The tool catalogue is cached in `%LOCALAPPDATA%\Prism\mcp-tools-cache.json`, so listing tools works before the app is running.

## Prompts

The bridge offers ready-made scenarios as MCP prompts:

| Prompt | What it does |
| --- | --- |
| `build_solid_part` | Model a solid part with sketches and features |
| `build_lofted_surface` | Build a lofted surface |
| `build_freeform_model` | Shape a freeform model |
| `test_2d_sketcher` | Exercise the 2D sketcher |
| `test_3d_sketch` | Exercise 3D sketching |
| `bug_hunt` | Explore the app looking for problems |

## Tips for good results

- Prefer `run_script` and batch many operations into one script, printing only what you need.
- Use `solid_find` (or `cad.faces` / `cad.edges`) to pick faces and edges by geometry rather than guessing indices.
- Use `screenshot` with `viewport: true` and a `max_width` to judge a model cheaply.
- `run_command` reports what changed: created and changed bodies, volume and area deltas, face and edge counts, and a warning if a result is invalid.
