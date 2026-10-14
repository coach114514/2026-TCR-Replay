"""Package source-faithful sweeper saved geometry with a scene-derived fold rig."""
from pathlib import Path
import json,re,collections
import numpy as np
from scipy.spatial.transform import Rotation
from match_replay.tools.solidworks_display import streams,assembly,cached_meshes,descriptor_tables
from match_replay.tools.robot_mesh import write_mesh,simplify_painted,armor_anchor
BASE=Path.home()/'.cache/tcr-robot-models/sweeper-source/扫地机器人v2'
DEST=Path('match_replay/unity/Assets/StreamingAssets/replay/assets/robots/sweeper');DEST.mkdir(parents=True,exist_ok=True)
data=streams(BASE/'总的1.0.SLDASM');occ=assembly(data);bindings,meshes,diagnostics=cached_meshes(data)
exclude=re.compile(r'轴承|垫[片圈]|齿[轮圈]|螺[钉丝母栓柱]|小头径|圆柱销|杯头|圆头M|平头M|密封圈|M[2345]-\d|^WFNANM|卡簧|^PRT|^RS\d|^04_EA|^05_EA|SRDINBAN|NUT|电管垫高柱子|主控模块|电源管理|调速器|电调|电路|PCB|排针|插座|达秒|芯片|嘉立创|小弹丸',re.I)
vs=[[],[],[],[]];ts=[[],[],[],[]];cs=[[],[],[],[]];offset=[0,0,0,0];missing=[];omitted=[];retained=[];fallback=[];construction=[];armor=[]
for x in occ:
    name=x['name']
    if x['hidden'] or exclude.search(name):omitted.append(name);continue
    binding=bindings.get(x['path']);mesh=None
    if binding:mesh=meshes.get(binding['hash']) or meshes.get(binding['previous'])
    if mesh is None:
        # Resolve only unique paths or identical duplicate native part files.
        filename=x['sourcePath'].replace('\\','/').split('/')[-1];candidates=list(BASE.rglob(filename))
        if len(candidates)>1:
            suffix=x['sourcePath'].replace('\\','/').split('/')[-2:];matches=[p for p in candidates if list(p.parts[-2:])==suffix]
            if len(matches)==1:candidates=matches
            else:
                import hashlib
                hashes={hashlib.sha256(p.read_bytes()).hexdigest() for p in candidates}
                if len(hashes)==1:candidates=candidates[:1]
        if len(candidates)==1:
            try:native=streams(candidates[0])
            except ValueError as e:
                missing.append({'name':name,'path':x['path'],'reason':str(e)});continue
            parts=[]
            for key,p in native.items():
                if key=='Contents/DisplayLists' or key.startswith('FaceTessellations/000-'):
                    parts.extend((v,t) for _,v,t in descriptor_tables(p))
            if parts:
                v=[];t=[];off=0
                for points,faces in parts:v.append(points);t.append(faces+off);off+=len(points)
                mesh=(np.concatenate(v),np.concatenate(t));fallback.append({'name':name,'file':str(candidates[0].relative_to(BASE))})
        if mesh is None:missing.append({'name':name,'path':x['path'],'sourcePath':x['sourcePath']});continue
    v,t=mesh
    if '装甲模块AM02' in name:
        # The official armor CAD also includes large planar measurement/view
        # envelopes. They are not the physical armor plate. Retain its detailed
        # 134 mm body, discard triangles whose edges exceed the plate envelope.
        p=v[t];edges=np.stack((p[:,1]-p[:,0],p[:,2]-p[:,1],p[:,0]-p[:,2]),axis=1)
        physical=(np.linalg.norm(edges,axis=2).max(1)<.16)
        construction.append({'name':name,'nonphysicalEnvelopeTrianglesOmitted':int((~physical).sum())})
        t=t[physical];ids,back=np.unique(t,return_inverse=True);v=v[ids];t=back.reshape((-1,3))
        armor.append((v,x['matrix']))
    m=x['matrix']
    if name=='中国石油大学枪管-final(1)':gun_matrix=m.copy()
    if name=='_17mm_云台pitch轴zq':pitch_matrix=m.copy();pitch_points=v.copy()
    v=v@m[:3,:3].T+m[:3,3]
    if np.max(np.abs(v))>2:raise ValueError('Retained source part is outside the car: '+name)
    group=0
    if x['path'].startswith('装配体2-2@总的1.0/'):
        # Source includes one saved pose. Lower link turns at its source axis;
        # upper carrier follows the matching four-bar arc and stays level.
        group=1 if name in ['zq云台z向链路','zq-偏航过渡链路-1','折叠云台主动推杆'] else 2
        if any(n in name for n in ['yaw底板','垫高下面']):group=0
        if '/直驱云台装配体-' in x['path']:group=3
    color=[125,130,136]
    if any(n in name for n in ['玻纤','CF-','保护','底面弹舱','壳','电气','碳','UWB','相机','RoboMaster','NUC','nuc','盖子']):color=[34,39,45]
    if any(n in name for n in ['滚子','轮胎','摩擦轮']):color=[22,26,30]
    vs[group].append(v);ts[group].append(t+offset[group]);cs[group].append(np.tile(color,(len(v),1)).astype('u1'));offset[group]+=len(v);retained.append(name)
for i in range(4):vs[i]=np.concatenate(vs[i]);ts[i]=np.concatenate(ts[i]);cs[i]=np.concatenate(cs[i])
# The four wheel axles lie in native XY; source native +Z points downwards.
# Use the chassis's greatest Z as contact datum and XY centre as origin.
chassis=vs[0];center=(chassis.min(0)+chassis.max(0))/2;center[2]=chassis[:,2].max()
axis=np.array([[1,0,0],[0,0,-1],[0,1,0]],float);scene=np.eye(4);scene[:3,:3]=axis;scene[:3,3]=-axis@center
pivot_native=np.array([-.0971,-.0867,.9783]);pivot=scene[:3,:3]@pivot_native+scene[:3,3]
# Scene-rule visualization, not a reconstructed mate solution or logged joint angle.
angle=86.;rotation_native=Rotation.from_rotvec(np.array([0,1,0])*np.deg2rad(angle)).as_matrix();r=axis@rotation_native@axis.T
upper=pivot_native+[-.0151,.0003,-.0988];shift=axis@((rotation_native@(upper-pivot_native)+pivot_native)-upper)
result={'schema':1,'id':'sweeper','name':'扫地机器人 v2.1','source':'总的1.0.SLDASM（已保存装配树和显示缓存；少量虚拟引用未恢复）','sourceSha256':'149abb5ddb1985a2ca141150766cdb4837af20c9efc3acb6c5239dcd5d5a7171','defaultFold':0,'poseProvenance':'原图外观；洞口收缩为场景推导，单姿态源文件未提供完整关节求解','groups':[],'lodTriangles':[0,0,0],'retainedSourceComponents':sorted(set(retained)),'omittedSourceComponents':sorted(set(omitted)),'unresolvedSourceComponents':missing,'nativeCacheDiagnostics':diagnostics,'directPartFallbacks':fallback,'constructionGeometryOmitted':construction}
result['armorPanels']=[]
for v,m in armor:
    panel=armor_anchor(v,m,scene)
    if not any(np.linalg.norm(np.array(panel['position'])-p['position'])<.001 for p in result['armorPanels']):result['armorPanels'].append(panel)
total=sum(len(t) for t in ts)
for i in range(4):
    paths=[];reports=[]
    for lod,budget in enumerate([100000,28000,6000]):
        v,t,c=simplify_painted(vs[i],ts[i],cs[i],max(12,round(budget*len(ts[i])/total)));v=v@scene[:3,:3].T+scene[:3,3]
        path=DEST/f'group-{i}-lod-{lod}.tcrmesh';report=write_mesh(path,v,t,c);reports.append(report);paths.append('assets/robots/sweeper/'+path.name);result['lodTriangles'][lod]+=report['triangles']
    quat=Rotation.from_matrix(r).as_quat() if i==1 else [0,0,0,1]
    result['groups'].append({'name':['chassis','lower-link','upper-carrier','gun-pitch'][i],'turret':i>0,'pitch':i==3,'meshes':paths,'pivot':pivot.tolist() if i==1 else [0,0,0],'foldRotation':list(quat),'foldTranslation':shift.tolist() if i>=2 else [0,0,0],'lod':reports})
    print('SWEEPER_GROUP',i,len(ts[i]),[r['triangles'] for r in reports],flush=True)
result['groundOffsets']=[0,0]
result['gunGroup']=3
result['muzzlePoint']=(scene@gun_matrix@np.array([0,.085,.0176,1]))[:3].tolist()
forward=scene[:3,:3]@gun_matrix[:3,:3]@np.array([0,1,0]);result['gunForward']=(forward/np.linalg.norm(forward)).tolist()
p=(pitch_points.min(0)+pitch_points.max(0))/2;result['pitchPivot']=(scene@pitch_matrix@np.append(p,1))[:3].tolist()
result['yawPivot']=(scene@np.array([-.0958946,.002762,1.03083065,1]))[:3].tolist()
result['pitchMin']=-30;result['pitchMax']=30
result['anchorProvenance']='saved barrel end and pitch-shaft bounds; saved chassis yaw plate; conservative display pitch limits'
result['eyeGroup']=2;result['eyePoint']=[0,.30,.11];result['labelPoint']=[0,.50,0]
(DEST/'model.json').write_text(json.dumps(result,ensure_ascii=False,indent=2));print('ROBOT_SWEEPER_OK',result['lodTriangles'],'unresolved',len(missing),flush=True)
