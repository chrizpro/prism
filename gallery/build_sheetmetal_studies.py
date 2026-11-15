import argparse, os
from pathlib import Path
import build_examples as study
import build_lotus_dish as lotus
cad=study.cad
p=argparse.ArgumentParser();p.add_argument('--discovery', default=str(Path(os.environ['LOCALAPPDATA'])/'Prism/automation.json'));args=p.parse_args()
bridge,log=study.connect_mcp(args.discovery)
try:
 lotus.HALF=42.;lotus.STRAIGHT=35.;lotus.TIP=52.;lotus.TIP_HALF=22.;lotus.RISE=65.;lotus.CURL=-38.
 lotus.build()
 cad.get('Lotus dish').rename('Radiant folded sculpture')
 cad.get('Lotus dish flat pattern').rename('Radiant flat pattern')
 cad.save(str(study.ROOT/'artifacts/gallery/radiant-checkpoint.prism'))
 study.verify_and_save('radiant-fold',-35,32,distance=850)
finally:
 bridge.terminate();bridge.wait(timeout=10);log.close()
