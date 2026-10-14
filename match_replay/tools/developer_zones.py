"""Digitize rule figure 5-24 against CAD base/outpost anchors (28 x 15 m)."""
import json
from pathlib import Path
root=Path(__file__).resolve().parents[1]
# PDF page106 rendered at1700px. Inner field edges x177..1043,y268..731.
def xy(p):return [round((p[0]-177)/866*28,4),round((731-p[1])/463*15,4)]
def rect(x1,y1,x2,y2):return [(x1,y1),(x2,y1),(x2,y2),(x1,y2)]
zones=[]
def add(id,team,kind,points,group='',order=0):
    polygon=[xy(p) for p in points];zones.append(dict(id=id,team=team,kind=kind,polygon=[v for pair in polygon for v in pair],group=group,order=order))
    if team=='red':zones.append(dict(id=id.replace('red','blue'),team='blue',kind=kind,polygon=[v for x,z in polygon for v in (round(28-x,4),round(15-z,4))],group=group.replace('red','blue'),order=order))
add('red_supply','red','supply',[(207,642),(273,642),(287,657),(287,730),(220,730),(207,711)])
add('red_base','red','base',[(202,465),(255,437),(309,465),(309,533),(255,561),(202,533)])
add('red_fortress','red','fortress',[(345,500),(363,468),(401,468),(419,500),(401,532),(363,532)])
add('red_trapezoid','red','trapezoid',[(275,364),(346,364),(333,398),(275,398)])
add('red_outpost','red','outpost',[(499,589),(526,589),(547,617),(530,646),(489,646),(509,619)])
add('red_assembly','red','assembly',rect(553,305,574,327))
# Two connected highland strips, each with an independently occupied upper card.
add('red_highland_low','red','crossing',rect(436,456,461,544),'red_highland',0)
add('red_highland_top','red','central',[(499,453),(553,358),(568,358),(568,388),(523,460),(523,545),(499,545)],'red_highland',1)
add('red_road_low','red','crossing',rect(417,675,448,690),'red_road',0)
add('red_road_high','red','crossing',rect(417,647,448,666),'red_road',1)
add('red_fly_low','red','crossing',rect(488,703,521,728),'red_fly',0)
add('red_fly_high','red','crossing',rect(666,703,699,728),'red_fly',1)
for letter,boxes in [('a',[(455,633,476,654),(455,657,476,668),(455,673,476,695)]),('b',[(607,671,628,694),(631,671,642,694),(646,671,667,694)])]:
    for order,box in enumerate(boxes):add(f'red_tunnel_{letter}_{order}','red','crossing',rect(*box),f'red_tunnel_{letter}',order)
config=dict(schema=1,source='Rule V2.0.1 figure5-24 printed p105 / PDF106, pixel digitization registered to official CAD base and outpost anchors',
    precision='Approximate 32mm/pixel. Card active areas digitized from schematic; no hardware dead-zone emulation. Query car center on matching ground level; folded pose region is independent.',zones=zones)
(root/'unity/Assets/StreamingAssets/replay/developer_zones.json').write_text(json.dumps(config,ensure_ascii=False,indent=2))
print('DEVELOPER_ZONES_OK',len(zones))
