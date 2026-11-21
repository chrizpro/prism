"""Compose Prism's exported help renders and explicit diagrams for every reference page.

Requires Pillow. Run after Prism.DocsImages; raw working data can live on another drive.
python tools/build_doc_tool_images.py --work E:/Prism-docs-work
"""
import argparse
import hashlib
import json
import math
import textwrap
from html import escape
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--work', type=Path, required=True)
args = parser.parse_args()
WORK = args.work
OUT = ROOT / 'site/public/images/tools'
OUT.mkdir(parents=True, exist_ok=True)
pages = json.loads((WORK/'tool-pages.json').read_text(encoding='utf-8'))
export = json.loads((WORK/'help-examples/help-examples.json').read_text(encoding='utf-8'))
helps = {p['slug']:p for p in export['pages']}
registry = {}
cache_file = WORK/'image-build-cache.json'
cache = json.loads(cache_file.read_text(encoding='utf-8')) if cache_file.exists() else {}
generator_hash = hashlib.sha256(Path(__file__).read_bytes()).digest()
BLUE, GOLD, GRAY = '#68d5ee', '#ffbd78', '#90a2b8'

def line(d, color=BLUE, fill='none', extra=''):
    return f'<path d="{d}" stroke="{color}" fill="{fill}" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" {extra}/>'

def txt(x,y,s,size=18,color='#e6eef8'):
    return f'<text x="{x}" y="{y}" fill="{color}" font-size="{size}">{escape(s)}</text>'

def rect(x,y,w,h,color='#24485c'):
    return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="5" fill="{color}" stroke="{BLUE}" stroke-width="2"/>'

def circle(x,y,r,color=BLUE,fill='none'):
    return f'<circle cx="{x}" cy="{y}" r="{r}" stroke="{color}" fill="{fill}" stroke-width="3"/>'

def arrow(x,y,dx,dy,color=GOLD):
    angle=math.atan2(dy,dx);x2=x+dx;y2=y+dy
    return line(f'M{x} {y}L{x2} {y2}M{x2-10*math.cos(angle-.5)} {y2-10*math.sin(angle-.5)}L{x2} {y2}L{x2-10*math.cos(angle+.5)} {y2-10*math.sin(angle+.5)}',color)

def block(x=80,y=45):
    return line(f'M{x} {y+35}l65 -30 70 28 -65 32Z',BLUE,'#315a70')+line(f'M{x} {y+35}v65l70 33v-68M{x+70} {y+133}l65 -30v-68',BLUE,'#213c51')

def file_icon(ext):
    return line('M100 25H170L205 60V155H100Z',BLUE,'#20364d')+line('M170 25V60H205')+txt(112,111,ext.upper(),25)

def tree(grouped=True):
    s=rect(40,32,212,145,'#18283d')
    if grouped: s+=txt(56,62,'▾ Group',20)+line('M70 72V139M70 90H92M70 128H92',GRAY)
    for i,name in enumerate(['Body A','Body B']):
        s+=rect(95 if grouped else 59,78+38*i,15,15)+txt(121 if grouped else 85,92+38*i,name,18)
    return s

def window(content=''):
    return rect(27,24,246,152,'#18283d')+line('M27 48H273',GRAY)+circle(41,36,3)+circle(54,36,3)+content

def mesh():
    s=block()
    return s+line('M80 80L150 145L145 50M150 145L215 108L150 113M110 66L180 96L180 157',GRAY)

def normal(flip=False):
    return line('M55 115L150 65L240 105L150 155Z',BLUE,'#24485c')+arrow(150,108,0,55 if flip else -65)

def diagram(slug, label):
    """Explicit recipes only. A new unillustrated tool must add a recipe, never a generic fallback."""
    alias={'group-sketch':'group','ungroup-sketch':'ungroup','duplicate-ecad':'duplicate','look-at-view':'look-at',
           'save-as-home':'save-as','flip-normals':'reverse-normal','export-step-ecad':'export-step',
           'import-board-step':'import-step','import-from-file':'import-mold','from-open-tab':'insert-component',
           'edit-play':'new-animation'}
    s=alias.get(slug,slug)
    if s in ['fillet-sketch','chamfer-sketch','mirror-sketch','mirror-and-weld','spring-actuator','force-torque','pipe-network','post-g-code','image-carve']:
        if s in ['fillet-sketch','chamfer-sketch']:
            before=line('M65 160V55H240')
            after=line('M65 160V95Q65 55 105 55H240') if s=='fillet-sketch' else line('M65 160V100L110 55H240')
            return before,after,'Two sketch lines','Rounded corner' if s=='fillet-sketch' else 'Straight bevel','Sketch '+label+' edits the corner between two lines in the sketch plane.'
        if s=='mirror-sketch':return line('M150 30V185',GRAY,extra='stroke-dasharray="6 6"')+line('M55 150V65H100V150Z'),line('M150 30V185',GRAY,extra='stroke-dasharray="6 6"')+line('M55 150V65H100V150Z M245 150V65H200V150Z'),'Curves + mirror line','Reflected sketch curves','Pick curves, finish selecting, then pick the mirror line. Live mode keeps both sides symmetric.'
        if s=='mirror-and-weld':return line('M150 35V185',GRAY,extra='stroke-dasharray="6 6"')+line('M150 65H90V155H150',BLUE,'#24485c'),line('M90 65H210V155H90Z M150 65V155',BLUE,'#24485c')+circle(150,65,5,GOLD)+circle(150,155,5,GOLD),'Half a cage','Reflected and joined at seam','Mirror and weld joins matching vertices along the mirror plane; this is a cage edit.'
        if s=='spring-actuator':return rect(45,65,45,85)+rect(215,65,45,85),rect(45,65,45,85)+rect(215,65,45,85)+line('M90 110H105L113 87L129 133L145 87L161 133L177 87L193 133L201 110H215',GOLD),'Two attachment points','Spring / damper / actuator','Connect points to add a spring, damper or actuator to a motion study. This is a setup diagram, not a simulated result.'
        if s=='force-torque':return block(),block()+arrow(260,110,-60,0)+line('M110 65A40 22 0 1 1 180 65L170 55M180 65L191 57',GOLD),'Motion body','Force and turning moment','Set force or torque direction and its constant, pulsed or alternating load. Arrows describe inputs, not solved motion.'
        if s=='pipe-network':
            network=line('M35 110H145L245 55M145 110L245 165')+circle(35,110,8)+circle(145,110,8)+circle(245,55,8)+circle(245,165,8)
            return network,network+arrow(65,110,45,0)+arrow(177,92,35,-19)+arrow(177,129,35,19),'Pipes joined at nodes','Flow splits at a junction','Pipe Network calculates steady node pressures and branch flows. Arrows are schematic, not calculated values.'
        if s=='post-g-code':return line('M55 150V65H230V150H55 M75 130V85H210V130H75'),window(txt(45,78,'G54',19)+txt(45,106,'G0 X... Y... Z...',17)+txt(45,134,'G1 X... Y... F...',17)),'Reviewed toolpaths','Posted program','Post reviewed paths using the controller dialect. This abbreviated program is illustrative, not machine-ready code.'
        if s=='image-carve':
            shades=''.join(rect(45+i*42,50,42,115,f'#{v:02x}{v:02x}{v:02x}') for i,v in enumerate([230,180,120,60,180]))
            return shades,line('M45 65H85V85H127V120H169V155H211V85H255',GOLD)+line('M45 180H255',GRAY),'Image brightness','Depth varies across the stock','Image Carve converts image brightness to cutting depth; the sketch shows a conceptual cross-section.'
    formats={'step-step':'STEP','iges-iges':'IGES','brep-brep':'BREP','sketch-dxf-dxf':'DXF','gltf-glb':'GLB',
             '3mf-3mf':'3MF','stl-stl':'STL','obj-obj':'OBJ','ply-ply':'PLY','prism-document-prism':'PRISM'}
    if s.startswith('export-') or s in formats:
        ext=formats.get(s,s.removeprefix('export-').upper());ext='GLB' if ext=='GLTF' else ext
        before=mesh() if ext in ['STL','OBJ','PLY','GLB','3MF'] else line('M65 65H235V150H65Z')+circle(150,108,25) if ext=='DXF' else block()
        note={'DXF':'Sketch curves for 2D exchange','STEP':'Exact geometry for CAD exchange','IGES':'Exact solids and surfaces','BREP':'Open CASCADE exact geometry',
              'GLB':'A scene for viewers and the web','3MF':'Separate bodies for a slicer','STL':'A triangle mesh for printing','OBJ':'A Wavefront mesh','PLY':'A mesh for scan workflows','PRISM':'An editable document copy'}[ext]
        return before,file_icon(ext),'Geometry in Prism',ext+' file',note
    if s in ['new','open','save','save-as','home','import-file','import-step','import-mesh','import-mold','import-point-cloud']:
        if s=='home':return window(rect(44,65,56,40)+rect(119,65,56,40)+rect(194,65,56,40)),window(txt(50,87,'Recent parts',20)+txt(50,125,'Workspace folder',18)),'Home screen','Choose where to start','Open a recent part or start from your workspace.'
        if s=='new':return window(block(80,45)),window(),'Current document','New empty document','Start a new model in an empty document.'
        if s=='open':return file_icon('prism'),window(block(80,40)),'Saved document','Model opened in Prism','Open a saved document to continue working.'
        if s in ['save','save-as']:return window(block(80,40)),file_icon('prism')+txt(42,190,'new name' if s=='save-as' else 'current name',16),'Editable model','Saved document','Save As chooses another name; Save writes the current document.'
        ext={'import-step':'STEP','import-mesh':'STL','import-mold':'PRISM_MLD','import-point-cloud':'XYZ','import-file':'FILE'}[s]
        result=mesh() if s=='import-mesh' else block()
        if s=='import-point-cloud':result=''.join(circle(70+i*18,65+j*20,2) for i in range(10) for j in range(5))
        return file_icon(ext),result,'Source file','Imported geometry','Import adds the supported geometry to the document; exchange files do not recreate Prism history.'
    if s in ['group','ungroup']:return tree(s=='ungroup'),tree(s=='group'),'Browser before','Browser after','A group adds a parent row; ungrouping keeps the objects.'
    if s in ['duplicate','insert-component','create-part','make-part','edit-part','refresh','parts-library','ground']:
        before=block(80,35);after=block(45,45)+f'<g transform="translate(90 -15)">{block(45,45)}</g>'
        if s=='ground':after=before+line('M95 178H200M100 178l-8 12M120 178l-8 12M140 178l-8 12M160 178l-8 12M180 178l-8 12',GOLD)
        if s=='make-part':after=tree(True).replace('Group','Part')
        if s=='create-part':before=tree(True).replace('Group','Assembly');after=window(block(80,40))
        if s=='edit-part':before=tree(True).replace('Group','Assembly');after=window(block(80,40))+txt(60,200,'Design workspace',16)
        if s=='refresh':before=file_icon('prism');after=block()+arrow(45,90,30,0)
        if s=='parts-library':before=window(rect(40,60,58,38)+rect(120,60,58,38)+rect(200,60,58,38));after=block()
        labels={'duplicate':('Selected object','Another copy'),'insert-component':('Part file or open tab','Placed component'),'create-part':('Assembly','New part document'),
                'make-part':('Solid bodies','Assembly parts'),'edit-part':('Selected component','Edit the part'),'refresh':('Changed source part','Reload in the assembly'),
                'parts-library':('Library thumbnails','Insert a chosen part'),'ground':('Free component','Fixed component')}
        a,b=labels[s];return before,after,a,b,{'ground':'Ground anchors the component while joints move connected parts.','duplicate':'Copies are shown apart for clarity; position them as needed.'}.get(s,'Components connect the assembly to its part geometry.')
    if s in ['gizmo','transform']:return block(),block()+arrow(150,115,80,0,'#ef6f70')+arrow(150,115,-50,40,'#75ce8b')+arrow(150,115,0,-75,'#70a6f9'),'Selection','Move, turn or scale','Colored axes set the direction. Use the gizmo or enter exact values.'
    if s in ['create-sketch','finish-sketch','cancel','finish-editing','done','close-picture']:
        outline=line('M80 80H200V145H80Z')+circle(140,110,20)
        if s=='create-sketch':return normal(),outline,'Pick a plane or face','Draw on its sketch plane','Sketch geometry can be constrained and dimensioned.'
        if s=='cancel':return outline,window(),'Open sketch','Discard sketch','Cancel leaves sketch editing without keeping the sketch.'
        return window(outline),block(),'Active editor','Back to modeling',{'finish-sketch':'Finish Sketch keeps the sketch.','finish-editing':'Finish Editing leaves Direct Edit.','done':'Done closes the Materials tab.','close-picture':'Close Picture hides the render and returns to the model.'}[s]
    if s in ['draw-on-surface','convert-to-nurbs','check-seam','reverse-normal','check-cage','check-and-repair','check-clearance','place-on-board','place-on-bed','insert-point','fix']:
        if s=='reverse-normal':return normal(),normal(True),'Original normal','Reversed normal','The arrow changes which side faces out; the surface is not moved.'
        if s=='draw-on-surface':return normal(),normal()+line('M80 112Q120 60 155 107T220 112',GOLD),'Surface','Curve drawn on the surface','Click points to draw across connected faces; the result can also split faces.'
        if s=='convert-to-nurbs':return mesh(),normal()+line('M100 91L190 134M147 67L150 154',GRAY),'Source geometry','Editable NURBS geometry','Convert supported geometry into exact NURBS for further CAD work.'
        if s=='check-seam':return line('M40 130Q90 50 150 100M150 100Q215 150 260 70'),line('M40 130Q90 50 150 100M150 100Q215 150 260 70')+circle(150,100,17,GOLD)+txt(52,180,'gap · tangent · curvature',16),'Neighboring patches','Inspect sampled seam quality','Check Seam samples compatible natural boundaries; it is not a global certificate.'
        if s in ['check-cage','check-and-repair']:return mesh()+circle(150,95,24,GOLD),mesh()+txt(60,195,'Inspect → choose repairs',16),'Geometry to inspect','Review topology or defects','Checks report geometry problems; review the report before repairing.'
        if s=='check-clearance':return rect(55,70,70,90)+rect(175,70,70,90),rect(55,70,70,90)+rect(175,70,70,90)+arrow(128,108,43,0)+txt(95,190,'gap vs required',17),'Two solids','Closest gap','Compare the measured closest distance with the required clearance.'
        if s=='place-on-board':return rect(40,140,220,18)+rect(115,40,70,50),rect(40,140,220,18)+rect(115,90,70,50),'Pick board face','Component seated on face','The upright component is seated on the picked flat board face.'
        if s=='place-on-bed':return block(80,20)+line('M30 190H270',GRAY),block(80,47)+line('M30 180H270',GOLD),'Part above the bed','Picked face at Z = 0','Set a CAD solid down using the face you choose.'
        if s=='insert-point':return line('M50 120Q150 30 250 120'),line('M50 120Q150 30 250 120')+circle(150,75,6,GOLD,'#ffbd78'),'Existing curve','Point inserted','The curve gains an editing point; a line is divided into two segments.'
        if s=='fix':return line('M70 130L230 70')+circle(70,130,5),line('M70 130L230 70',GOLD)+rect(60,142,24,24)+line('M65 142V132Q72 119 79 132V142',GOLD),'Movable geometry','Position locked','Fix constrains the selected point or curve in the sketch.'
    if s in ['import-image','edit-image','set-scale']:
        picture=rect(45,45,210,125,'#28405d')+line('M60 150L120 85L155 125L190 70L240 150',GRAY)+circle(95,70,10)
        after=picture
        if s=='edit-image':after=f'<g transform="translate(35 -10) rotate(12 150 100) scale(.8)">{picture}</g>'
        if s=='set-scale':after+=line('M60 185H240M60 178V192M240 178V192',GOLD)+txt(115,210,'50 mm',17)
        return file_icon('PNG') if s=='import-image' else picture,after,'Reference picture','Placed / adjusted reference','Set Scale uses two picked points and their known real distance.' if s=='set-scale' else 'Pictures are references for tracing, placement and measurement.'
    if s in ['ar-export','stop-sharing']:return block(),rect(110,25,90,170,'#192b40')+(block(75,42) if s=='ar-export' else line('M125 65L185 145M185 65L125 145',GOLD)),'Desktop model','Share to phone' if s=='ar-export' else 'Sharing stopped','AR Export serves the phone page over your Wi-Fi; Stop Sharing ends that server.'
    if s in ['script-editor','new-script','run-script','vs-code','pycharm','scripts-folder']:
        code=window(txt(44,80,'import cadkit as cad',16)+txt(44,110,'cad.box(width=30)',16))
        return file_icon('PY') if s=='run-script' else window(),code,'Script file' if s=='run-script' else 'Your document',{'vs-code':'Open in VS Code','pycharm':'Open in PyCharm','scripts-folder':'Scripts and examples','new-script':'Start from an example','script-editor':'Editor and console','run-script':'Run a Python script'}[s],'Python scripts use cadkit to create and inspect Prism geometry.'
    if s=='constant-z-finish':return block(),block()+''.join(line(f'M75 {95+i*13}l70 30 75 -35',GOLD) for i in range(4)),'Steep walls','Contours at successive Z','Constant-Z Finish follows steep walls with horizontal contour passes.'
    if s in ['print-check','clear-colors','show-mode','load-from-motion']:
        if s=='clear-colors':return block()+circle(150,95,35,GOLD,'#c57c52'),block(),'Analysis colors','Normal model shading','Clear Colors removes mesh analysis coloring.'
        if s=='show-mode':return line('M50 130H250'),line('M50 130Q130 10 250 130',GOLD)+line('M50 130H250',GRAY,extra='stroke-dasharray="5 5"'),'Undeformed shape','Vibration mode shape','A mode shape illustrates a vibration pattern, not actual displacement amplitude.'
        if s=='load-from-motion':return block()+arrow(255,100,-45,0),block()+arrow(255,100,-45,0,'#f18d8a')+txt(55,195,'Static study load',18),'Calculated joint reaction','Transfer reaction to Static','Use a calculated motion reaction as a load for a separate structural study.'
        return mesh(),mesh()+rect(20,25,80,45)+txt(30,54,'CHECK',16),'Mesh to print','Review print checks','Check watertightness, build fit, support needs and weight before export.'
    if s=='parts-list':return block()+f'<g transform="translate(65 0)">{block()}</g>',window(txt(45,80,'Part       Qty',20)+txt(45,115,'Bracket     2',18)+txt(45,147,'Pin         1',18)),'Assembly components','Parts list / CSV','Count parts and review material, mass and cost from their stored information.'
    if s in ['new-animation','render-animation']:
        timeline=line('M35 120H265',GRAY)+''.join(line(f'M{x} 112V130',GRAY) for x in range(35,266,38))+''.join(line(f'M{x} 98l8 8 -8 8 -8 -8Z',GOLD,GOLD) for x in [55,135,230])
        return timeline,window(block(80,35)) if s=='render-animation' else timeline+arrow(135,70,65,0),'Timeline keyframes','Render frames / GIF / MP4' if s=='render-animation' else 'Scrub and play','Animation interpolates keyed joint values or the exploded view; MP4 export needs ffmpeg.'
    if s in ['sun','lamp','spot','edit-lights','show-lights']:
        rays=''.join(arrow(60+i*70,30,20,65) for i in range(3)) if s=='sun' else ''.join(line(f'M150 55L{x} 150',GOLD) for x in [55,105,195,245])
        if s=='spot':rays=line('M150 40L75 155H225Z',GOLD)
        return block(),block()+rays+circle(150,35,9,GOLD),'Model','Light direction / position','Sun gives directional lighting.' if s=='sun' else 'Spot lights a cone.' if s=='spot' else 'Lamp brightness uses lumens; distance changes illumination.' if s=='lamp' else 'Show light markers to select, position or edit the document lights.'
    if s in ['render-image','save-picture']:return block(),window(block(80,35))+txt(70,201,'PNG picture',18),'Viewport','Rendered picture' if s=='render-image' else 'Save last render','Render produces a picture; Save Picture writes the last render as PNG.'
    if s in ['matte','satin','glossy','brushed-metal','polished-metal','apply-material','remove-material','find-assigned','compare','clear-compare']:
        if s in ['compare','clear-compare']:
            table=window(txt(42,80,'Material  A   B   C',16)+txt(42,115,'Density  ... ... ...',15)+txt(42,145,'Cost     ... ... ...',15))
            return table if s=='clear-compare' else block(),window() if s=='clear-compare' else table,'Material library','Empty comparison' if s=='clear-compare' else 'Compare up to 3 materials','Compare stored material properties side by side; Clear Compare empties the table.'
        swatch='<defs><radialGradient id="ball"><stop stop-color="#cbe4ef"/><stop offset=".55" stop-color="#5d90ad"/><stop offset="1" stop-color="#203347"/></radialGradient></defs>'+circle(150,105,64,BLUE,'url(#ball)')
        if s in ['glossy','polished-metal','satin']:swatch+='<ellipse cx="131" cy="77" rx="'+('26' if s=='satin' else '13')+'" ry="10" fill="#f3f8ff" opacity="'+('.3' if s=='satin' else '.9')+'"/>'
        if s=='brushed-metal':swatch+=''.join(line(f'M105 {70+i*8}H195',GRAY) for i in range(10))
        return circle(150,105,64,GRAY,'#557289'),swatch,'Current appearance',{'apply-material':'Apply library material','remove-material':'Remove material assignment','find-assigned':'Find assigned material'}.get(s,label),'Finish comparison is schematic, not a measured render. Material assignment also supplies engineering data.' if s not in ['apply-material','remove-material','find-assigned'] else 'Appearance and engineering material data are related but distinct; see the material controls.'
    if s=='zoom-selected':return window(block(80,35)+circle(225,150,13,GOLD,'#c57c52')),window(circle(150,105,58,GOLD,'#c57c52')),'Selection small in the view','Selection fills the view','Zoom Selected centres the selected bodies, faces or edges and fills the view with them.'
    if s in ['front','back','left','right','top','bottom','isometric','look-at','zoom-extents','set-home','single','two-views','four-views','link-views','grid','data-table']:
        if s in ['single','two-views','four-views','link-views']:
            n=1 if s=='single' else 4 if s in ['four-views','link-views'] else 2
            after=rect(30,30,240,155)
            if n>1:after+=line('M150 30V185',GRAY)
            if n==4:after+=line('M30 108H270',GRAY)
            for i in range(n):after+=txt(44+(i%2)*120,72+(i//2)*78,['Model','Top','Front','Right'][i],16)
            if s=='link-views':after+=arrow(130,145,35,0)
            return window(block(80,35)),after,'Model view',label,'Linked views share panning and zooming.' if s=='link-views' else 'Choose the viewport layout for the views you need.'
        if s=='data-table':return file_icon('CSV'),window(txt(45,80,'x     y     z',20)+txt(45,115,'0     0     0',18)+txt(45,145,'20   10     5',18)),'Table data','Document Data panel','Use a named sheet as @sheet-name for supported points, positions or motion inputs.'
        if s=='grid':return block(),block()+''.join(line(f'M{30+i*30} 175L{110+i*15} 80M{30+i*30} 100L{80+i*25} 200',GRAY) for i in range(7)),'Grid hidden','Construction grid shown','The grid is a drawing reference; showing it does not change the model.'
        face=line('M75 65H225V160H75Z',BLUE,'#28465d')+circle(150,110,22)
        if s in ['top','bottom']:face=line('M65 85H235V150H65Z',BLUE,'#28465d')+circle(150,118,22)
        if s in ['left','right']:face=line('M105 45H195V175H105Z',BLUE,'#28465d')+circle(150,110,22)
        if s=='isometric':face=block()
        if s in ['front','back','left','right','top','bottom']:
            direction={'front':'from −Y','back':'from +Y','left':'from −X','right':'from +X','top':'from +Z','bottom':'from −Z'}[s]
            face+=txt(105,200,direction,18,GOLD)
        if s in ['zoom-extents','set-home']:face=window(block(80,35))
        return block(),face,'Three-quarter model',label+' view' if s not in ['zoom-extents','set-home'] else label,'Standard views look along a model axis. These schematic projections show the viewing direction.' if s not in ['zoom-extents','set-home','look-at'] else 'Fit the model to the view.' if s=='zoom-extents' else 'Set Home stores your chosen model orientation.' if s=='set-home' else 'Look normal to the selected plane, face or sketch.'
    raise ValueError('No explicit diagram recipe for '+slug)

def svg_for(page):
    slug=page['slug'];label=page['tool']['label']
    a,b,before,after,note=diagram(slug,label)
    body=f'<g transform="translate(22 83)">{a}</g><g transform="translate(395 83)">{b}</g>'
    title=escape(label)
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="760" height="370" viewBox="0 0 760 370" role="img" aria-labelledby="title desc">'
            f'<title id="title">{title}: {escape(before)} to {escape(after)}</title><desc id="desc">{escape(note)}</desc>'
            f'<rect width="760" height="370" rx="15" fill="#101d30"/><g font-family="Segoe UI, Arial, sans-serif">{txt(25,35,label,24)}'
            f'{txt(25,75,before,18,GRAY)}{txt(398,75,after,18,GOLD)}{body}{arrow(350,185,30,0)}'
            f'{txt(25,349,"EXPLANATORY DIAGRAM · NOT AN APPLICATION SCREENSHOT",12,GRAY)}</g></svg>'), note

font_path=Path('C:/Windows/Fonts/segoeui.ttf')
font=ImageFont.truetype(str(font_path),18)
titlefont=ImageFont.truetype(str(font_path),23)

for page in pages:
    slug=page['slug'];h=helps[slug];topic=h.get('topic')
    explicit={'fillet-sketch','chamfer-sketch','mirror-sketch','mirror-and-weld','spring-actuator','force-torque','pipe-network','post-g-code','image-carve'}
    if h.get('picture') and slug not in explicit:
        name=h['picture'];target=OUT/(name+'.webp')
        before_data=(WORK/'help-examples'/f'{name}-before.bgra').read_bytes()
        after_data=(WORK/'help-examples'/f'{name}-after.bgra').read_bytes()
        fingerprint=hashlib.sha256(before_data+after_data+json.dumps(topic,sort_keys=True).encode()+generator_hash).hexdigest()
        before=Image.frombytes('RGBA',(560,320),before_data,'raw','BGRA').convert('RGB')
        after=Image.frombytes('RGBA',(560,320),after_data,'raw','BGRA').convert('RGB')
        canvas=Image.new('RGB',(1160,440),'#101d30');draw=ImageDraw.Draw(canvas)
        draw.text((20,12),topic['Title'],font=titlefont,fill='#e6eef8')
        for x,label in [(20,topic['BeforeLabel']),(600,topic['AfterLabel'])]:
            for row,text in enumerate(textwrap.wrap(label,width=49)[:2]):draw.text((x,49+row*24),text,font=font,fill='#ffbd78' if x>20 else '#c1cfdf')
        canvas.paste(before,(20,106));canvas.paste(after,(600,106))
        # Equivalent topics share one asset; captions remain specific to the reference page.
        previous=cache.get(target.name,{})
        if not target.exists() or previous.get('source')!=fingerprint or previous.get('asset')!=hashlib.sha256(target.read_bytes()).hexdigest():
            canvas.save(target,'WEBP',quality=88,method=6)
        cache[target.name]=dict(source=fingerprint,asset=hashlib.sha256(target.read_bytes()).hexdigest())
        registry[slug]=dict(src='/images/tools/'+target.name,width=1160,height=440,kind='example',
                            alt=f"{page['tool']['label']}: {topic['BeforeLabel']} compared with {topic['AfterLabel']}.",
                            caption=topic['Purpose']+' '+topic['Tip'],topic=h['key'])
        if page['places'][0]['tab'] in ['Static','Thermal','Dynamics','Motion','Fluids']:
            registry[slug]['caption']='Schematic help example; colors and deformation are illustrative, not results from a solved study. '+registry[slug]['caption']
    else:
        svg,note=svg_for(page);target=OUT/(slug+'.svg');target.write_text(svg,encoding='utf-8',newline='\n')
        registry[slug]=dict(src='/images/tools/'+target.name,width=760,height=370,kind='diagram',
                            alt=page['tool']['label']+': '+note,caption=note)
    registry[slug]['sha256']=hashlib.sha256(target.read_bytes()).hexdigest()

capture_file = WORK/'captures/result-captures.json'
captures = json.loads(capture_file.read_text(encoding='utf-8')) if capture_file.exists() else []
capture_map = {'curve-from-data':'helix-result', 'sweep':'scaled-sweep-result', 'sew':'sew-merge-result',
               'fillet-chamfer':'surface-fillet-result', 'split-face-direct-modeling':'imprint-result'}
by_name = {c['name']:c for c in captures}
for slug,name in capture_map.items():
    if name not in by_name:
        continue
    record = by_name[name]
    source = WORK/'captures'/f'{name}.png'
    assert hashlib.sha256(source.read_bytes()).hexdigest() == record['sha256'], source
    picture = Image.open(source).convert('RGB')
    target = OUT/(name+'.webp')
    picture.save(target,'WEBP',lossless=True,method=6)
    registry[slug] = dict(src='/images/tools/'+target.name,width=picture.width,height=picture.height,kind='capture',
                          alt=record['caption'],caption=record['caption'],sha256=hashlib.sha256(target.read_bytes()).hexdigest(),
                          originalSha256=record['sha256'])

(ROOT/'site/data/tool-images.json').write_text(json.dumps(registry,indent=2,ensure_ascii=False)+'\n',encoding='utf-8',newline='\n')
provenance=dict(sourceRevision='fba5314',helpAssemblySha256=export['assemblyHash'],kernelAssemblySha256=export['kernelAssemblyHash'],
                generatedUtc=export['generatedUtc'],pages=len(registry),distinctHelpExamples=export['examples'],
                method='ToolHelp.Example geometry rendered with StudioPreview.Render; explicit SVG recipes for non-preview actions. Not command-execution or solver evidence.')
provenance['resultCaptures'] = {slug:name for slug,name in capture_map.items() if name in by_name}
provenance['captureMethod'] = 'Actual commands in an isolated Windows desktop instance; screenshot(viewport=true), fixed isometric view. Raw states, native documents and call records retained in the supplied work directory.'
native = ROOT/'native/bin/prism_kernel.dll'
if native.exists():
    provenance['nativeKernelSha256'] = hashlib.sha256(native.read_bytes()).hexdigest()
(ROOT/'site/data/tool-image-provenance.json').write_text(json.dumps(provenance,indent=2)+'\n',encoding='utf-8',newline='\n')
cache_file.write_text(json.dumps(cache,indent=2),encoding='utf-8')
print(f'Illustrated {len(registry)} pages: {sum(i["kind"]=="example" for i in registry.values())} help examples, {sum(i["kind"]=="diagram" for i in registry.values())} diagrams, {sum(i["kind"]=="capture" for i in registry.values())} actual results.')
