# HTTP protocol

The automation endpoint is plain HTTP and JSON, so any language can use it. `cadkit` and the MCP bridge are both thin clients of this protocol.

## Find the running app

On start, Prism writes a discovery file:

```text
%LOCALAPPDATA%\Prism\automation.json
```

It holds the `port`, a random `token` for this run, and the process `pid`. Set `PRISM_AUTOMATION_DISCOVERY` to an absolute path, in both the app and the client, to use a separate file, for example for an isolated test instance.

Scripts started from Prism's own script editor also receive `CADKIT_PORT` and `CADKIT_TOKEN` in their environment.

## Requests

Every request must send the token in the `X-Prism-Token` header. Requests without it, or with any `Origin` header, get `403`.

| Method and path | Body | Result |
| --- | --- | --- |
| `GET /ping` | | `{ "ok": true, "pid": 1234 }` |
| `GET /tools` | | `{ "ok": true, "tools": [ ... ] }`: every [automation tool](/api/automation) with its JSON schema |
| `POST /call` | `{ "tool": "name", "args": { ... } }` | `{ "ok": true, "result": ... }` or `{ "ok": false, "error": "message" }` |
| `POST /script` | `{ "code": "...", "timeout_seconds": 120, "atomic": true }` | `{ "ok": true, "result": { "exit_code", "timed_out", "rolled_back", "stdout", "stderr" } }` |

`/script` runs Python against the live document. With `atomic` (the default), a script that fails or times out is rolled back, so the document is left as it was. The timeout is clamped to 1 to 300 seconds.

Tools that return a picture, such as `screenshot`, answer with `{ "_image": { "mime": "image/png", "data": "<base64>" } }`.

## Python with the standard library

```python
import json, os, urllib.request

d = json.load(open(os.path.expandvars(r"%LOCALAPPDATA%\Prism\automation.json")))

def call(tool, **args):
    req = urllib.request.Request(
        f"http://127.0.0.1:{d['port']}/call", method="POST",
        data=json.dumps({"tool": tool, "args": args}).encode(),
        headers={"X-Prism-Token": d["token"], "Content-Type": "application/json"})
    r = json.load(urllib.request.urlopen(req))
    if not r["ok"]:
        raise RuntimeError(r["error"])
    return r["result"]

call("run_command", name="box", params={"Width": 40, "Depth": 30, "Height": 20})
print(call("get_state")["objects"])
```

## C\#

```csharp
using System.Net.Http.Json;
using System.Text.Json.Nodes;

var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prism", "automation.json");
var d = JsonNode.Parse(File.ReadAllText(path))!;
var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{d["port"]}") };
http.DefaultRequestHeaders.Add("X-Prism-Token", d["token"]!.GetValue<string>());

var reply = await http.PostAsJsonAsync("/call", new { tool = "run_command", args = new { name = "box", @params = new { Width = 40 } } });
var json = await reply.Content.ReadFromJsonAsync<JsonObject>();
Console.WriteLine(json!["ok"]!.GetValue<bool>() ? json["result"] : json["error"]);
```

## Batches

`batch` runs several tool calls in one round trip, which is useful when latency matters. See [`batch`](/api/automation#batch).

## Timing

Tools run on the UI thread. A very slow kernel operation, such as a large plate-surface fill, blocks other calls until it finishes. After view changes, call `wait_idle` before sending pointer events or taking screenshots, so they see the final camera.
