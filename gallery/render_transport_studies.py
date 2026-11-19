"""Open each generated native study, verify solids and render the saved geometry."""
import argparse
import time
import build_examples as study

views = {
    "vermilion-gt": (-33, 13, [-180, 0, 480], 5300),
    "tideline-jet": (-48, 6, [-800, -800, 4000], 45500),
    "evergreen-express": (-41, 6, [-6200, 0, 1650], 46000),
}

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("model", choices=views)
    parser.add_argument("--discovery", required=True)
    args = parser.parse_args()
    bridge, log = study.connect_mcp(args.discovery)
    cad = study.cad
    try:
        name = args.model
        path = study.ROOT / "gallery/models" / (name + ".prism")
        cad.call("file", action="open", path=str(path))
        objects = cad.state()["objects"]
        assert objects and all(o["kind"] == "Solid" for o in objects)
        for obj in objects:
            info = cad.call("object_info", object=obj["name"])
            assert info["valid"] and info["volume"] > 0, obj["name"]
        print(f"{name}: reopened {len(objects)} valid objects", flush=True)
        yaw, pitch, target, distance = views[name]
        cad.call("run_command", name="render", commit=False)
        cad.call("view", yaw=yaw, pitch=pitch, target=target, distance=distance, grid=False)
        cad.call("command_set", params={
            "Picture size": "HD  1280 \u00d7 720", "Quality (samples per pixel)": 96,
            "Lighting": "Studio (soft white boxes)", "Light bounces": 5,
            "Background": "White", "Ground plane with shadows": True,
            "Denoise final image": True, "Start render": True,
        })
        deadline = time.monotonic() + 900
        while time.monotonic() < deadline:
            status = cad.call("render_control", action="status")
            if status.get("error"):
                raise RuntimeError(status["error"])
            if status.get("finished"):
                break
            time.sleep(2)
        assert status["finished"], status
        cad.call("render_control", action="save", path=str(study.ROOT / "gallery/previews" / (name + ".png")))
        cad.call("render_control", action="close")
        cad.call("command_cancel")
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
