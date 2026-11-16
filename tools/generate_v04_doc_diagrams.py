"""Rebuild the explanatory SVGs for the 0.4 modeling guide (no CAD screenshots)."""
import json
import math
from pathlib import Path
from html import escape

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'site/public/images/diagrams'

def path(d, color='#5ee0ee', fill='none', extra=''):
    return f'<path d="{d}" stroke="{color}" fill="{fill}" stroke-width="3" stroke-linejoin="round" {extra}/>'

def label(x, y, text, size=17):
    return f'<text x="{x}" y="{y}" fill="#e7edf7" font-size="{size}">{escape(text)}</text>'

def panel(x, title, drawing, note):
    return f'<g transform="translate({x} 60)"><rect width="218" height="216" rx="12" fill="#182638"/>{label(14, 30, title)}{drawing}{label(14, 195, note, 13)}</g>'

items = {}
def save(name, title, panels, caption, alt):
    body = ''.join(panel(17+i*234, *p) for i,p in enumerate(panels))
    svg = f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 720 300" width="720" height="300" role="img" aria-labelledby="title desc"><title id="title">{escape(title)}</title><desc id="desc">{escape(alt)}</desc><rect width="720" height="300" rx="16" fill="#0e1929"/><g font-family="Segoe UI, Arial, sans-serif">{label(23, 36, title, 23)}{body}</g></svg>\n'
    (OUT / f'v04-{name}.svg').write_text(svg, encoding='utf-8')
    items[f'diagram-v04-{name}'] = dict(src=f'/images/diagrams/v04-{name}.svg', alt=alt, caption=caption, width=720, height=300)

square = path('M35 150V65H180V150Z', fill='#244d63')
save('boundary', 'Boundary Surface: choose the job', [
    ('Fill boundary', square, 'Close a loop.'),
    ('Edge', path('M30 150Q100 90 185 125 M30 75Q100 40 185 65') + path('M30 150L30 75L185 65L185 125Z', fill='#244d63'), 'Span 2 to 4 curves.'),
    ('Network', square + path('M35 105Q100 70 180 105 M105 65Q80 110 105 150', '#ffbb70'), 'Outline + inside curves.')
], 'Automatic chooses from your inputs. Explicit modes make the intended construction clear; Blend bridges two surface edges.', 'Three drawings compare a filled loop, a span between curves and a boundary with crossing interior guide curves.')
save('sew', 'Sew: joined edges or fewer faces?', [
    ('Two sheets', path('M30 70H100V150H30Z M115 70H185V150H115Z', fill='#244d63'), 'Pick adjacent surfaces.'),
    ('Stitch', square + path('M105 65V150', '#ffbb70'), 'Shared edge; 2 faces.'),
    ('Merge', square, 'Same surface; 1 face.')
], 'Merge can remove a seam between faces on the same underlying surface. A crease stays; unsuccessful merges remain stitched.', 'Two separate planar sheets become a stitched sheet with a middle edge, or one merged planar face without the edge.')
save('chamfer', 'Chamfer: three ways to size a bevel', [
    ('Equal distance', path('M35 155V95L95 55H180') + label(40,80,'d') + label(80,120,'d'), 'Same leg on both sides.'),
    ('Two distances', path('M35 155V125L145 55H180') + label(40,110,'2 mm') + label(105,95,'4 mm'), 'Set each leg separately.'),
    ('Distance + angle', path('M35 155V110L125 55H180') + label(40,95,'d') + label(100,105,'angle'), 'Swap sides if needed.')
], 'Cross-section sketches, not to scale. Style selects the dimensions used to define the cut.', 'Three bevel cross-sections show equal legs, unequal legs and a leg plus angle.')
save('imprint', 'Imprint: mark a face without removing material', [
    ('Pick curves', square + '<circle cx="108" cy="108" r="27" fill="none" stroke="#ffbb70" stroke-width="3"/>', 'Circle on a solid face.'),
    ('Closed loop', square + '<circle cx="108" cy="108" r="27" fill="#6b58ab" stroke="#ffbb70" stroke-width="3"/>', 'A new disc face; no cut.'),
    ('Open line', square + path('M35 110H120', '#ffbb70'), 'An edge; no new face.')
], 'Split Face → Mode → Imprint works on solids in this branch. A closed loop can divide the face; an interior-ending line does not make another face.', 'A circle drawn on a planar face marks a disc region, while a line ending inside the face adds an edge without enclosing a region.')
helix = 'M' + ' L'.join(f'{108+65*math.cos(t*math.pi*8):.2f} {155-100*t+13*math.sin(t*math.pi*8):.2f}' for t in [i/240 for i in range(241)])
save('helix', 'Helix: a path first, a sweep second', [
    ('Curve from Data', path(helix) + path('M108 45V168', '#8b9bb4', extra='stroke-dasharray="5 5"'), 'Points from → Helix.'),
    ('Set the rise', path('M55 150V65M48 65H62M48 150H62', '#ffbb70') + label(75,95,'pitch × turns') + label(75,125,'10 × 5 = 50 mm'), 'Height along the axis.'),
    ('Use in Sweep', path(helix, '#427d90', extra='style="stroke-width:12"') + path(helix), 'Add a closed profile.')
], 'Choose X, Y or Z, radius, pitch, turns and handedness. End radius 0 keeps the starting radius; a different end radius tapers the helix.', 'A helix around its axis, the pitch-times-turns height calculation and a thickened path illustrating a later sweep.')
save('sweep', 'Sweep: control the size along the path', [
    ('Start and end', path('M30 70L185 95V125L30 150Z', fill='#244d63'), 'Scale 1 → 0.5.'),
    ('Scale table', path('M30 95Q105 35 185 100L185 120Q105 180 30 125Z', fill='#244d63'), '0:1, 0.5:1.6, 1:0.7'),
    ('Read each pair', label(23,92,'0.5 : 1.6',25) + label(23,125,'halfway : 1.6×',18), 'Table replaces end scales.')
], 'The optional scale table uses normalized path position and positive scale. A second rail steers rotation; it does not stretch the section to reach that rail.', 'A taper from full to half scale, a bulging variable section and the interpretation of the midpoint scale pair.')
save('fillet', 'Fillet: radii follow the picked edges', [
    ('Pick order', path('M35 150V65H180', '#ffbb70') + label(45,120,'1') + label(120,90,'2'), 'One entry for each edge.'),
    ('Radius per edge', label(22,100,'3, 5-8, 2',26) + label(22,135,'edge 2: 5 → 8 mm',16), 'Extra edges use Radius.'),
    ('Two surfaces', path('M35 65V120Q35 150 65 150H185', fill='none') + path('M35 120Q35 150 65 150', '#ffbb70'), 'Pick a touching edge each.')
], 'A range varies radius along one edge. Two touching separate surfaces can be joined and dressed; borders alone and non-touching sheets are refused. Setback is not available.', 'An ordered edge selection, a per-edge radius list and a rounded junction between two perpendicular sheets.')

save('draft', 'Parting-line draft: split crossing walls first', [
    ('Split at the plane', path('M50 55V160H170V55Z', fill='#244d63') + path('M25 108H195', '#ffbb70', extra='stroke-dasharray="6 4"'), 'Upper and lower faces.'),
    ('Positive angle', path('M70 55L50 108L70 160H150L170 108L150 55Z', fill='#244d63') + path('M25 108H195', '#ffbb70', extra='stroke-dasharray="6 4"'), 'Both ends narrow away.'),
    ('Before confirming', label(18,85,'Neutral plane',19) + label(18,117,'+ separate wall faces',15) + label(18,149,'+ draft angle',17), 'Centers decide the side.')
], 'An exaggerated cross-section illustrates opposite draft directions. Walls that cross the plane need splitting first; Draft does not split them for you.', 'A straight-sided part split at its middle plane and a drafted part that narrows toward both ends.')
save('analyze', 'Analyze: choose what you want to inspect', [
    ('Reflection flow', ''.join(path(f'M30 {75+i*18}Q100 {45+i*18} 185 {75+i*18}', '#5ee0ee') for i in range(5)), 'Zebra Stripes.'),
    ('Surface curvature', ''.join(f'<rect x="{30+i*26}" y="65" width="26" height="90" fill="{c}"/>' for i,c in enumerate(['#4264bd','#408fae','#50b59e','#b4bd68','#e9a266','#db766e'])), 'Curvature Map.'),
    ('Other checks', label(18,85,'Draft Analysis',18) + label(18,120,'Check Seam',18), 'Angles and seam quality.')
], 'Surface and NURBS share the Analyze dropdown. These are schematic inspection examples, not measured results or a real curvature legend.', 'Stylized zebra stripes, a curvature color strip and the Draft Analysis and Check Seam menu entries.')
save('keep', 'Keep the originals: check before confirming', [
    ('Original sheets', path('M30 75H90V150H30Z M125 75H185V150H125Z', fill='#244d63'), 'Inputs to Sew.'),
    ('Off (default)', square, 'Only the joined result.'),
    ('On', path('M30 60H90V95H30Z M125 60H185V95H125Z', '#ffbb70') + path('M30 115H185V155H30Z', fill='#244d63'), 'Sources + joined result.')
], 'Sew and Thicken default to replacing their inputs. Intersection Curve replaces two flat source curves by default but never removes reference planes, faces or bodies.', 'Separate input sheets, a result alone with retention off and originals plus result with retention on; shown apart for clarity.')

registry = ROOT / 'site/data/guide-diagrams.json'
data = json.loads(registry.read_text(encoding='utf-8'))
data.update(items)
registry.write_text(json.dumps(data, indent=2, ensure_ascii=True)+'\n', encoding='utf-8', newline='\n')
print(f'Generated and registered {len(items)} concept diagrams.')
