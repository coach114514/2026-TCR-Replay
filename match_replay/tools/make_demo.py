"""Generate deterministic synthetic matches and original primitive geometry.

No official dataset, CAD, rulebook images or external Python packages needed.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
from pathlib import Path
import shutil
import struct
import zlib
from .cli import validate, write_json

ROOT = Path(__file__).resolve().parents[1]


def png(path: Path):
    width, height = 560, 300
    rows = bytearray()
    for y in range(height):
        rows.append(0)
        for x in range(width):
            grid = x % 20 == 0 or y % 20 == 0
            color = (125, 133, 143) if grid else (72, 82, 95)
            if x < 6: color = (215, 70, 80)
            if x > width - 7: color = (60, 140, 225)
            rows.extend(color)
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>2I5B', width, height, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(rows)) + chunk(b'IEND', b''))


def geometry():
    vertices, indices = [], []
    def box(x, y, z, sx, sy, sz):
        base = len(vertices)
        vertices.extend((x + dx * sx / 2, y + dy * sy / 2, z + dz * sz / 2)
                        for dx, dy, dz in [(-1,-1,-1),(1,-1,-1),(1,-1,1),(-1,-1,1),(-1,1,-1),(1,1,-1),(1,1,1),(-1,1,1)])
        faces = [(0,1,2),(0,2,3),(4,6,5),(4,7,6),(0,4,5),(0,5,1),(1,5,6),(1,6,2),(2,6,7),(2,7,3),(3,7,4),(3,4,0)]
        indices.extend(base + i for face in faces for i in face)
    box(14,-.08,7.5,28,.16,15)
    box(14,.30,7.5,3,.60,3)
    box(7,.45,3,3,.90,.35)
    box(21,.45,12,3,.90,.35)
    return vertices, indices


def generate(output: Path):
    output.mkdir(parents=True, exist_ok=True)
    assets = output / 'assets'; assets.mkdir(exist_ok=True)
    vertices, indices = geometry()
    write_json(assets / 'demo_mesh.json', dict(positions=[v for p in vertices for v in p], indices=indices, source='original procedural demo geometry'))
    with (assets / 'demo_ground.tcrmesh').open('wb') as f:
        f.write(b'TCRMESH1' + struct.pack('<ii', len(vertices), len(indices)))
        for p in vertices: f.write(struct.pack('<3f', *p))
        for i in indices: f.write(struct.pack('<i', i))
    png(assets / 'demo_map.png')
    anchors = []
    for team in ('red','blue'):
        for role, x, y in [('base',2,7.5),('outpost',11,3.5)]:
            anchors.append(dict(team=team,role=role,name=('红' if team=='red' else '蓝')+'方示例'+('基地' if role=='base' else '前哨'),x=x if team=='red' else 28-x,y=y if team=='red' else 15-y,source='synthetic positions'))
    marks = [dict(team=team,role=role,name=team+' synthetic '+role,x=x if team=='red' else 28-x,y=y if team=='red' else 15-y)
             for team in ('red','blue') for role,x,y in [('fortress',6.5,7.5),('helipad',3,13)]]
    fields = []
    for n in (1,2):
        field_id = f'synthetic_demo_{n}'
        config = dict(id=field_id,name=f'程序生成示例场地 {n}（非官方）',width=28,depth=15,calibration='synthetic',notes='Synthetic demo only; not official field geometry or a real match.',rulesVersion='synthetic demo',texture='assets/demo_map.png',mesh='assets/demo_mesh.json',meshFloorHeight=0,meshMaxDisplayHeight=3,collisionMesh='assets/demo_ground.tcrmesh',buildings=anchors,landmarks=marks,tunnels=[])
        write_json(output/f'field_{n}.json',config)
        fields.append(dict(id=field_id,name=config['name'],file=f'field_{n}.json'))
    write_json(output/'fields_catalog.json',dict(schema=1,defaultField=fields[0]['id'],fields=fields))
    write_json(output/'resource_profile.json',dict(schema=1,kind='synthetic-demo',notice='Original synthetic matches and primitive geometry; no official assets.'))
    entries = []
    for game in (1,2):
        entities = []
        for team in ('red','blue'):
            for j,role in enumerate(['base','outpost','hero','infantry3','infantry4','sentry']):
                samples = []
                for t in range(1,421):
                    building=role in ('base','outpost')
                    x=5+j*.7+1.5*math.sin(t*.035+game+j);y=4+j*.75+1.5*math.cos(t*.03+j)
                    if team=='blue': x,y=28-x,15-y
                    max_hp=5000 if role=='base' else 1500 if role=='outpost' else 200
                    samples.append(dict(t=t,x=round(x,5),y=round(y,5),z=0,valid=not building,gun=45 if team=='red' else 225,hasGun=not building,hp=max_hp,maxHp=max_hp,hasHp=True,hasMaxHp=True,hasAmmo17=not building,ammo17=t//30,flags=['synthetic_demo']))
                entities.append(dict(id=team+'_'+role,name=role,role=role,team=team,school='合成红队' if team=='red' else '合成蓝队',robotId=j+1,samples=samples))
        events=[dict(id=f'demo-{game}-{t}-{team}',kind='发弹',entity=team+'_infantry3',team=team,t=t,value=1,hasValue=True,category='17mm',note='synthetic demonstration only') for t in range(30,421,30) for team in ('red','blue')]
        data=dict(schema=1,gameId=f'demo-{game}',region='合成示例',redSchool='合成红队',blueSchool='合成蓝队',winner='未知',startTime='2026-10-15T00:00:00',source='synthetic-demo; not a real match',fieldId=fields[0]['id'],matchNumber=game,round=1,duration=420,timeResolution=1,entities=entities,events=events)
        validate(data)
        name=f'matches/demo-{game}.json';write_json(output/name,data)
        entries.append(dict(gameId=data['gameId'],file=name,region=data['region'],redSchool=data['redSchool'],blueSchool=data['blueSchool'],winner='未知',startTime=data['startTime'],seriesKey=f'demo-series-{game}',matchNumber=game,round=1,duration=420,sha256=hashlib.sha256((output/name).read_bytes()).hexdigest(),tags=[],metrics=[],bookmarks=[],quality=[],ruleVersion='synthetic demo'))
    write_json(output/'catalog.json',dict(schema=1,series=2,source='synthetic-demo',matches=entries,schools=[dict(name='合成红队',aliases=['合成红队','demo-red']),dict(name='合成蓝队',aliases=['合成蓝队','demo-blue'])],tagDefinitions=[]))
    notices=ROOT.parent/'licenses'
    if notices.exists():
        shutil.copytree(notices,output/'licenses',dirs_exist_ok=True)
    write_json(output/'demo_manifest.json',dict(schema=1,source='original generator make_demo.py',files={str(p.relative_to(output)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(output.rglob('*')) if p.is_file() and p.name!='demo_manifest.json'}))


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,default=ROOT/'unity/Assets/StreamingAssets/replay')
    parser.add_argument('--force',action='store_true',help='Replace an existing synthetic demo; never overwrite external assets')
    args=parser.parse_args();out=args.output.resolve()
    if out.exists() and any(out.iterdir()):
        profile=out/'resource_profile.json'
        if not args.force or not profile.exists() or json.loads(profile.read_text()).get('kind')!='synthetic-demo':
            parser.error('Output is nonempty. Only an existing synthetic demo can be regenerated with --force.')
        shutil.rmtree(out)
    generate(out)
    print(f'SYNTHETIC_DEMO_OK {out}')


if __name__=='__main__':main()
