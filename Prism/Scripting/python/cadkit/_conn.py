"""Connection to the running application (a localhost HTTP endpoint; the standard library is enough)."""
import json
import os
import urllib.error
import urllib.request

_state = {"port": None, "token": None}

# the folder name is the same one the application writes its discovery file into
_APP_FOLDERS = ("Prism",)


class CadError(RuntimeError):
    """The application refused a request; the message is the one shown in the application."""


def _discover():
    # started from the application's script editor: it hands over the address in the environment
    port, token = os.environ.get("CADKIT_PORT"), os.environ.get("CADKIT_TOKEN")
    if port and token:
        return int(port), token
    base = os.environ.get("LOCALAPPDATA") or os.path.expanduser("~/.local/share")
    for folder in _APP_FOLDERS:
        path = os.path.join(base, folder, "automation.json")
        if os.path.exists(path):
            with open(path, encoding="utf-8") as f:
                d = json.load(f)
            return int(d["port"]), d["token"]
    raise CadError("The application is not running. Start it, then run the script again.")


def connect(port=None, token=None):
    """Point cadkit at an application instance. Normally not needed: it finds the running one by itself."""
    if port is None or token is None:
        port, token = _discover()
    _state["port"], _state["token"] = int(port), token


def call(tool, **args):
    """Call any application tool by name (the same tools the MCP bridge offers) and return its result."""
    if _state["port"] is None:
        connect()
    body = json.dumps({"tool": tool, "args": args}).encode("utf-8")
    req = urllib.request.Request(
        "http://127.0.0.1:%d/call" % _state["port"], data=body, method="POST",
        headers={"X-Prism-Token": _state["token"], "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=300) as r:
            reply = json.loads(r.read().decode("utf-8"))
    except urllib.error.URLError as e:
        _state["port"] = None                                   # the application may have restarted: look again next time
        raise CadError("Lost the connection to the application (%s)." % e.reason) from None
    if not reply.get("ok"):
        raise CadError(reply.get("error", "The application reported an error."))
    return reply.get("result")


def tools():
    """Return the application automation tools and their argument schemas."""
    if _state["port"] is None:
        connect()
    req = urllib.request.Request(
        "http://127.0.0.1:%d/tools" % _state["port"],
        headers={"X-Prism-Token": _state["token"]})
    with urllib.request.urlopen(req, timeout=30) as response:
        return json.load(response)["tools"]
