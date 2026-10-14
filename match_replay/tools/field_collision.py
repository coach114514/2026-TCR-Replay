"""Build separate ground-only colliders from each display field.

Tunnel locations are taken from the final CAD, checked against rules fig 4-35.
Regional geometry is a different source model: its cave regions are measured
independently, never copied from the final field without checking.
"""
from pathlib import Path
import json,struct
import numpy as np
from match_replay.tools.robot_mesh import write_mesh
ROOT=Path('match_replay/unity/Assets/StreamingAssets/replay')

def binary(path):
    p=path.read_bytes();nv,ni=struct.unpack_from('<II',p,8)
    return np.frombuffer(p,'<f4',nv*3,16).reshape((-1,3)).copy(),np.frombuffer(p,'<u4',ni,16+nv*12).reshape((-1,3)).copy()

def regions():
    red=[{'id':'red-tunnel-a','name':'红方公路隧道 A','team':'red','x':9.3535,'y':2.255,'halfX':.4,'halfY':.18,'clearance':.26},
         {'id':'red-tunnel-b','name':'红方公路隧道 B','team':'red','x':14.926,'y':1.537,'halfX':.175,'halfY':.4,'clearance':.25}]
    blue=[dict(p,id=p['id'].replace('red','blue'),name=p['name'].replace('红','蓝'),team='blue',x=28-p['x'],y=15-p['y']) for p in red]
    for p in red+blue:p.update({'holdMargin':.50,'approachMargin':1.15,'floorMax':.45 if p['id'].endswith('a') else .25,'source':'官方 STEP 地板、洞顶边界；规则 V2.0.1 图4-35','calibration':'cad_geometry'})
    return red+blue

for filename in ['final_2026.json','region_2026.json']:
    config=Path('match_replay/field_configs')/filename;field=json.loads(config.read_text());official=filename.startswith('final')
    if official:
        v,t=binary(ROOT/'assets/official_final/343127.tcrmesh');v+=[14,1.941343676495,5.875656323505];passages=regions()
    else:
        data=json.loads((ROOT/field['mesh']).read_text());v=np.array(data['positions']).reshape((-1,3));v[:,1]-=field['meshFloorHeight'];t=np.array(data['indices']).reshape((-1,3));passages=[]
    p=v[t];normal=np.cross(p[:,1]-p[:,0],p[:,2]-p[:,0]);area=np.linalg.norm(normal,axis=1);center=p.mean(1)
    keep=(normal[:,1]>.65*area)&(area>1e-5)&(p[:,:,1].max(1)<field.get('meshMaxDisplayHeight',3))&(p[:,:,1].min(1)>-.1)
    for tunnel in passages:
        # Crop the whole cover, including roof trim, while retaining the road/ramp below.
        in_cover=(np.abs(center[:,0]-tunnel['x'])<tunnel['halfX']+.27)&(np.abs(center[:,2]-tunnel['y'])<tunnel['halfY']+.1)
        keep&=~(in_cover&(center[:,1]>tunnel['floorMax']))
    # Remove the engineering details of the bumps; preserve road, ramps and steps.
    keep&=area>.0001
    faces=t[keep];ids,back=np.unique(faces,return_inverse=True);verts=v[ids];faces=back.reshape((-1,3));target=ROOT/'assets/collision'/('final_floor.tcrmesh' if official else 'regional_floor.tcrmesh');report=write_mesh(target,verts,faces)
    field['collisionMesh']=str(target.relative_to(ROOT));field['tunnels']=passages;field['collisionNotes']='独立地面层：CAD 上表面、坡面与台阶；移除全国赛洞顶，隐藏显示模型后仍保留碰撞。区域赛未包含可核对的同款洞口，不套用全国赛坐标。'
    config.write_text(json.dumps(field,ensure_ascii=False,indent=2)+'\n')
    runtime=ROOT/('fields/final_2026.json' if official else 'field_config.json');runtime.write_text(config.read_text())
    print('FLOOR_COLLISION_OK',filename,report,'tunnels',len(passages),flush=True)
