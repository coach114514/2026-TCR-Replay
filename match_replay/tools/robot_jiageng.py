"""Export the JiaGeng source CAD poses as shared rigid groups with three LODs.

Preparation cache: jiageng-parts/{expanded,folded,definitions}.json and per-label
.npz from OCP. Native Y is up; positions are mm until packaging.
"""
from pathlib import Path
import json,re,collections
import numpy as np
from match_replay.tools.robot_mesh import write_mesh,simplify_painted,rigid_blend,armor_anchor
CACHE=Path.home()/'.cache/tcr-robot-models/jiageng-parts'
DEST=Path('match_replay/unity/Assets/StreamingAssets/replay/assets/robots/jiageng')
DEST.mkdir(parents=True,exist_ok=True)
a=json.loads((CACHE/'expanded.json').read_text())['occurrences'];b={tuple(x['path']):x for x in json.loads((CACHE/'folded.json').read_text())['occurrences']}
# Hidden engineering hardware and PCB internals do not define the exterior.
exclude=re.compile(r'螺[钉母栓丝柱]|垫[圈片]|轴承|zz|销[轴钉]|齿[轮圈]|卡[簧环]|弹簧|杯头|平头|沉头|内六角|^_M[23456]|EA230.*(NUT|SR-|BUTTON|LOCK)|芯片|主控模块|电源管理|调速器|电调|PSDK|\.stp.*PCB|板载|排针|插座|连接器',re.I)
groups={};omitted=[];original=0;armor=[]
for x in a:
    path=CACHE/(x['key'].replace(':','_')+'.npz')
    if not path.exists() or exclude.search(x['name']):omitted.append(x['name']);continue
    y=b[tuple(x['path'])]
    if y['name']!=x['name']:raise ValueError('Source occurrence mismatch')
    m=np.array(x['matrix']);f=np.array(y['matrix']);delta=f@np.linalg.inv(m)
    turret=x['path'][0]==89 or x['path'][0] in (90,107,143)
    pitch=tuple(x['path'][:2])==(89,3)
    key=(turret,pitch,tuple(np.round(delta.flatten(),3)))
    data=np.load(path);v=data['positions'];t=data['indices'];c=data['colors'];original+=len(t)
    v=v@m[:3,:3].T+m[:3,3]
    if '装甲模块AM02' in x['name']:armor.append((data['positions'],m))
    if x['name']=='中国石油大学枪管-final(1)':gun_matrix=m.copy();gun_key=key
    if x['name']=='_17mm_云台pitch轴zq':pitch_matrix=m.copy();pitch_points=data['positions']
    if key not in groups:groups[key]={'delta':delta,'turret':turret,'pitch':pitch,'v':[],'t':[],'c':[],'offset':0,'sources':[]}
    g=groups[key];g['v'].append(v);g['t'].append(t+g['offset']);g['c'].append(c);g['offset']+=len(v);g['sources'].append(x['name'])
    # Preserve source CAD colors; exterior motors are near-black in the real photo.
    if any(n in x['name'] for n in ['碳','玻纤','电气仓','麦轮钣金','装甲模块AM02']):g['c'][-1]=np.tile([36,40,45],(len(v),1)).astype('u1')
for g in groups.values():
    for key in ['v','t','c']:g[key]=np.concatenate(g[key])
body=[g for g in groups.values() if not g['turret']]
chassis=np.concatenate([g['v'] for g in body]);folded=np.concatenate([g['v']@g['delta'][:3,:3].T+g['delta'][:3,3] for g in body])
floor=float(folded[:,1].min());center=(chassis.min(0)+chassis.max(0))/2;center[1]=floor
# Preserve the source pose; runtime aligns the measured gun-local +Y forward.
axis=np.array([[0,0,1],[0,1,0],[-1,0,0]],float)
scene=np.eye(4);scene[:3,:3]=axis*.001;scene[:3,3]=-axis@center*.001
result={'schema':1,'id':'jiageng','name':'嘉庚麦轮步兵','source':'整车模型-展开形态.STEP / 整车模型-折叠形态.STEP','sourceSha256':'f432450b4c65ab91a7075fb16e26fcf32127d6b07ec5d406839db797ed81b561','defaultFold':.5,'chassisPose':'source half pose away from tunnels; full lowered pose in tunnels','poseProvenance':'洞口姿态为场景规则推导；开源外观与历史真实车型未核对','groups':[],'originalRetainedTriangles':original,'lodTriangles':[0,0,0],'sourceComponentsOmitted':sorted(set(omitted))}
result['armorPanels']=[armor_anchor(v,m,scene) for v,m in armor]
for i,g in enumerate(groups.values()):
    meshpaths=[];report=[];total=sum(len(x['t']) for x in groups.values())
    for lod,budget in enumerate([100000,28000,6000]):
        target=max(12,round(budget*len(g['t'])/total));v,t,c=simplify_painted(g['v'],g['t'],g['c'],target)
        v=v@scene[:3,:3].T+scene[:3,3];path=DEST/f'group-{i}-lod-{lod}.tcrmesh';r=write_mesh(path,v,t,c);report.append(r);meshpaths.append('assets/robots/jiageng/'+path.name);result['lodTriangles'][lod]+=r['triangles']
    d=scene@g['delta']@np.linalg.inv(scene)
    pivot=np.linalg.lstsq(np.eye(3)-d[:3,:3],d[:3,3],rcond=1e-8)[0];residual=d[:3,3]-(np.eye(3)-d[:3,:3])@pivot
    from scipy.spatial.transform import Rotation
    quat=Rotation.from_matrix(d[:3,:3]).as_quat()
    result['groups'].append({'name':f'rigid-{i}','turret':g['turret'],'pitch':g['pitch'],'meshes':meshpaths,'pivot':pivot.tolist(),'foldRotation':quat.tolist(),'foldTranslation':residual.tolist(),'sourceComponents':g['sources'],'lod':report})
    print('ROBOT_GROUP',i,len(g['t']),[r['triangles'] for r in report],flush=True)
# Contact is normalized to the fully lowered pose; interpolate a sampled wheel
# support curve so the body height can change without pushing wheels below ground.
support=[]
for amount in np.linspace(0,1,33):
    vertices=[]
    for g in body:
        d=rigid_blend(g['delta'],amount);v=g['v']@d[:3,:3].T+d[:3,3];vertices.append(v@scene[:3,:3].T+scene[:3,3])
    v=np.concatenate(vertices);support.append(float(-v[:,1].min()))
result['groundOffsets']=support
result['gunGroup']=list(groups).index(gun_key)
result['muzzlePoint']=(scene@gun_matrix@np.array([0,85,17.6,1]))[:3].tolist()
result['gunForward']=(scene[:3,:3]@gun_matrix[:3,:3]@np.array([0,1,0])).tolist()
result['gunForward']=(np.array(result['gunForward'])/np.linalg.norm(result['gunForward'])).tolist()
pivot=(pitch_points.min(0)+pitch_points.max(0))/2
result['pitchPivot']=(scene@pitch_matrix@np.append(pivot,1))[:3].tolist()
# Rotation centre of the base plate's source yaw transform, in native mm.
yaw_group=next(g for g in groups.values() if g['turret'] and not g['pitch'])
d=yaw_group['delta'];yp=np.linalg.lstsq(np.eye(3)-d[:3,:3],d[:3,3],rcond=1e-8)[0]
result['yawPivot']=(scene@np.append(yp,1))[:3].tolist()
result['pitchMin']=-30;result['pitchMax']=30
result['anchorProvenance']='source barrel local end and pitch shaft; yaw centre from two saved poses; mechanical pitch range is conservative display constraint'
# Camera is above the source UWB mounting point, transformed with its own motion group.
uwb=next((i for i,g in enumerate(groups.values()) if any('UWB' in n for n in g['sources'])),1)
result['eyeGroup']=uwb;result['eyePoint']=[0,.34,.02];result['labelPoint']=[0,.52,0]
(DEST/'model.json').write_text(json.dumps(result,ensure_ascii=False,indent=2));print('ROBOT_JIAGENG_OK',result['lodTriangles'],floor,flush=True)
