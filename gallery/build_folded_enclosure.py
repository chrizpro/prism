import os
from pathlib import Path
import build_examples as study
import build_cardboard_box as box
cad=study.cad
bridge,log=study.connect_mcp(Path(os.environ['LOCALAPPDATA'])/'Prism/automation.json')
try:
 box.T=1.5;box.R=2.;box.K=.44;box.L=160.;box.W=110.;box.H=50.;box.LIP=22.;box.GAP=2.
 box.KRAFT=box.KRAFT_FLAT=dict(material='5052',color='#BEC4CA',metal=.95,roughness=.3,pattern='Brushed',patternScale=3)
 box.build()
 cad.get('Cardboard box').rename('Folded metal enclosure')
 cad.get('Box blank (die line)').rename('Enclosure flat pattern')
 cad.save(str(study.ROOT/'artifacts/gallery/enclosure-checkpoint.prism'))
 study.verify_and_save('folded-enclosure',-38,30,distance=950)
finally:
 bridge.terminate();bridge.wait(timeout=10);log.close()
