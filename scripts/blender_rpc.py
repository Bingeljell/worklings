"""Talk to a running Blender over its `execute_code` RPC.

Every Blender workflow in this project goes through here: character export,
kit-piece baking, scene inspection. Blender stays open with the file loaded and
we send it Python, rather than launching a headless Blender per operation —
which keeps a 30 MB .blend resident instead of re-reading it every time, and
lets a human watch what the script is doing.

Requires Blender running with the RPC addon listening (default port 9876).

    from blender_rpc import run
    print(run("import bpy; print(bpy.data.filepath)"))

Or from the shell:

    python3 scripts/blender_rpc.py < some_script.py

Two things worth knowing, both of which cost time to discover:

* **`bpy.ops` often needs a context override.** The RPC executes outside a
  normal UI context, so operators like `object.modifier_apply` and
  `export_scene.gltf` fail with "context is incorrect" or complain that
  `active_object` does not exist. Wrap them:

      win = bpy.context.window_manager.windows[0]
      with bpy.context.temp_override(window=win, screen=win.screen,
                                     object=obj, active_object=obj,
                                     selected_objects=[obj]):
          bpy.ops.object.modifier_apply(modifier="Dec")

* **Blender's API changes between versions.** This project is on 5.2, where
  Collada export was removed, actions are slotted (`action.layers[...]` rather
  than `action.fcurves`), and several operator keywords were renamed. Check
  `bpy.ops.<op>.get_rna_type().properties` rather than assuming.
"""

import json
import socket
import sys

HOST = "127.0.0.1"
PORT = 9876
TIMEOUT = 600


def send(command, params=None, timeout=TIMEOUT, host=HOST, port=PORT):
    """Send one command and return the raw JSON response text."""
    conn = socket.create_connection((host, port), timeout=timeout)
    conn.settimeout(timeout)
    try:
        conn.sendall(json.dumps({"type": command, "params": params or {}}).encode())
        chunks = b""
        while True:
            try:
                data = conn.recv(65536)
            except socket.timeout:
                break
            if not data:
                break
            chunks += data
            try:
                json.loads(chunks.decode())  # a complete document: stop reading
                break
            except ValueError:
                continue
        return chunks.decode()
    finally:
        conn.close()


def run(code, timeout=TIMEOUT):
    """Execute `code` inside Blender and return whatever it printed.

    Raises on transport or execution failure so a broken script does not read
    as an empty-but-successful run.
    """
    raw = send("execute_code", {"code": code}, timeout=timeout)
    try:
        payload = json.loads(raw)
    except ValueError:
        raise RuntimeError(f"Blender returned unparseable output: {raw[:500]}")
    if payload.get("status") != "success":
        raise RuntimeError(payload.get("message", raw[:500]))
    return payload.get("result", {}).get("result", "")


if __name__ == "__main__":
    print(run(sys.stdin.read()))
