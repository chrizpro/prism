"""Refresh older gallery studies with current Appearance controls and complete Prism renders."""
import argparse
import build_examples as study


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--discovery", required=True)
    args = parser.parse_args()
    bridge, log = study.connect_mcp(args.discovery)
    cad, root = study.cad, study.ROOT
    try:
        cad.call("document_new")
        cad.call("file", action="open", path=str(root / "gallery/models/glide-iron.prism"))
        looks = {
            "03 Glide - soleplate": dict(material="304", color="#7C8993", metal=1, roughness=.23,
                                        pattern="Brushed", patternScale=2, surface=dict(Anisotropy=.6)),
            "03 Glide - water housing": dict(material="hdpe", color="#2C98AD", roughness=.27,
                                        surface=dict(Gradient=1, Axis=2, Color2=0xFFB1E1DD, GradientSize=70, Coat=.5)),
            "03 Glide - soft grip": dict(material="silicone", color="#263447", roughness=.65, surface=dict(Bump=.12)),
            "03 Glide - temperature dial": dict(material="abs", color="#E5DDCF", roughness=.35, surface=dict(Coat=.25)),
            "03 Glide - water inlet": dict(material="abs", color="#E5DDCF", roughness=.35, surface=dict(Coat=.25)),
        }
        for name, appearance in looks.items():
            cad.call("material_apply", object=name, **appearance)
        study.verify_and_save("glide-iron", -32, 22)
        cad.call("document_new")
        cad.call("file", action="open", path=str(root / "gallery/models/transition-duct.prism"))
        cad.call("material_apply", object="N03 duct - NURBS wall", material="6061", color="#BECBD2", metal=1,
                 roughness=.3, pattern="Brushed", patternScale=4, surface=dict(Anisotropy=.35))
        cad.call("material_apply", object="N03 duct - mounting flange", material="6061", color="#285B65", metal=.6,
                 roughness=.33, surface=dict(Coat=.3))
        study.verify_and_save("transition-duct", -35, 25)
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
