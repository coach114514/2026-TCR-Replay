"""Export independently moving wheel modules from the original CAD caches.

Read-only CAD input. Existing replay meshes are never overwritten. Run with
the CAD virtual environment: python -m match_replay.tools.vehicle_rig.
"""
from pathlib import Path
from collections import defaultdict
import hashlib,json,re
import numpy as np
from scipy.spatial.transform import Rotation
from scipy.spatial import ConvexHull,HalfspaceIntersection
from .solidworks_display import streams,assembly,cached_meshes,descriptor_tables
from .robot_mesh import write_mesh,simplify_painted

ROOT=Path(__file__).resolve().parents[1]
DATA=ROOT/'unity/Assets/StreamingAssets/replay'
CACHE=Path.home()/'.cache/tcr-robot-models'
OUT=ROOT/'artifacts/vehicle-physics/mechanics'


def normalized(v):
    v=np.asarray(v,dtype=float)
    return v/np.linalg.norm(v)


def source_sweeper(model):
    base=CACHE/'sweeper-source/扫地机器人v2';source=base/'总的1.0.SLDASM'
    data=streams(source);occ=assembly(data);bindings,meshes,_=cached_meshes(data)
    axis=np.array([[1,0,0],[0,0,-1],[0,1,0]],float)
    shift=np.asarray(model['groups'][1]['pivot'])-axis@np.array([-.0971,-.0867,.9783])
    scene=np.eye(4);scene[:3,:3]=axis;scene[:3,3]=shift
    retained=set(model['retainedSourceComponents']);records=[];missing=[]
    for x in occ:
        name=x['name']
        if x['hidden'] or name not in retained:continue
        binding=bindings.get(x['path']);mesh=None
        if binding:mesh=meshes.get(binding['hash']) or meshes.get(binding['previous'])
        if mesh is None:
            filename=x['sourcePath'].replace('\\','/').split('/')[-1]
            candidates=list(base.rglob(filename))
            if len(candidates)>1:
                suffix=x['sourcePath'].replace('\\','/').split('/')[-2:]
                matches=[p for p in candidates if list(p.parts[-2:])==suffix]
                if len(matches)==1:candidates=matches
                elif len({hashlib.sha256(p.read_bytes()).hexdigest() for p in candidates})==1:candidates=candidates[:1]
            if len(candidates)==1:
                try:part=streams(candidates[0])
                except ValueError:part={}
                tables=[(v,t) for k,p in part.items() if k=='Contents/DisplayLists' or k.startswith('FaceTessellations/000-') for _,v,t in descriptor_tables(p)]
                if tables:mesh=join([(v,t,np.tile([125,130,136],(len(v),1))) for v,t in tables])[:2]
        if mesh is None:missing.append(x['path']);continue
        v,t=mesh;m=scene@x['matrix'];v=v@m[:3,:3].T+m[:3,3]
        if x['path'].startswith('装配体2-2@总的1.0/'):
            if not any(n in name for n in ['yaw底板','垫高下面']):continue
        if '装甲模块AM02' in name:continue
        color=[125,130,136]
        if any(n in name for n in ['玻纤','CF-','保护','底面弹舱','壳','电气','碳','UWB','相机','RoboMaster','NUC','nuc','盖子']):color=[34,39,45]
        if any(n in name for n in ['滚子','轮胎','摩擦轮']):color=[22,26,30]
        records.append(dict(path=x['path'],name=name,v=v,t=t,c=np.tile(color,(len(v),1)).astype('u1'),matrix=m))
    roots=sorted({x['path'].split('/')[0] for x in records if x['path'].startswith('OBJ775-')})
    assert len(roots)==4,roots
    result=defaultdict(list);wheels=[]
    for key in roots:
        members=[x for x in records if x['path'].split('/')[0]==key]
        tire=[x for x in members if '/COT-OTOW-全向轮装配' in x['path']]
        assert tire,key
        center=(np.concatenate([x['v'] for x in tire]).min(0)+np.concatenate([x['v'] for x in tire]).max(0))/2
        middle=next(x for x in tire if '全向轮中板' in x['name'])
        axle=normalized(middle['matrix'][:3,1]);axle*=1 if axle[0]>=0 else -1
        radius=max(np.linalg.norm((x['v']-center)-np.outer((x['v']-center)@axle,axle),axis=1).max() for x in tire)
        width=max(np.ptp(np.concatenate([x['v'] for x in tire])@axle),.02)
        index=len(wheels);wheels.append(dict(id=f'wheel-{index}',kind='slider',sourceRoot=key,center=center.tolist(),pivot=center.tolist(),axle=axle.tolist(),axis=[0,1,0],radius=float(radius),width=float(width),foldAngle=0,minTravel=-.005,maxTravel=.030,travelProvenance='CAD rail 85mm and block 45.4mm; conservative 5mm extension / 30mm compression, retained stop margins; mate limits unavailable',moduleMass=1.1,rotorMass=.45))
        for x in members:
            node='tire-'+str(index) if '/COT-OTOW-全向轮装配' in x['path'] else 'carrier-'+str(index)
            if any(n in x['name'] for n in ['定端','底部限位','末端挡','COT-导轨MGN12']):node='frame'
            result[node].append(x)
    for x in records:
        if not x['path'].startswith('OBJ775-'):result['frame'].append(x)
    return result,wheels,scene,missing


def source_jiageng(model):
    cache=CACHE/'jiageng-parts';a=json.loads((cache/'expanded.json').read_text())['occurrences']
    b={tuple(x['path']):x for x in json.loads((cache/'folded.json').read_text())['occurrences']}
    exclude=re.compile(r'螺[钉母栓丝柱]|垫[圈片]|轴承|zz|销[轴钉]|齿[轮圈]|卡[簧环]|弹簧|杯头|平头|沉头|内六角|^_M[23456]|EA230.*(NUT|SR-|BUTTON|LOCK)|芯片|主控模块|电源管理|调速器|电调|PSDK|\.stp.*PCB|板载|排针|插座|连接器',re.I)
    records=[];body=[];folded=[];groupkeys={}
    for x in a:
        file=cache/(x['key'].replace(':','_')+'.npz')
        if not file.exists() or exclude.search(x['name']):continue
        d=np.load(file);m=np.array(x['matrix']);fm=np.array(b[tuple(x['path'])]['matrix']);delta=fm@np.linalg.inv(m)
        turret=x['path'][0]==89 or x['path'][0] in (90,107,143);pitch=tuple(x['path'][:2])==(89,3)
        key=(turret,pitch,tuple(np.round(delta.flatten(),3)));g=groupkeys.setdefault(key,len(groupkeys))
        v=d['positions']@m[:3,:3].T+m[:3,3]
        if not turret:body.append(v);folded.append(v@delta[:3,:3].T+delta[:3,3])
        if turret or '装甲模块AM02' in x['name']:continue
        c=d['colors'].copy()
        if any(n in x['name'] for n in ['碳','玻纤','电气仓','麦轮钣金']):c=np.tile([36,40,45],(len(v),1)).astype('u1')
        records.append(dict(path=x['path'],name=x['name'],v=v,t=d['indices'],c=c,matrix=m,local=d['positions'],group=g))
    points=np.concatenate(body);center=(points.min(0)+points.max(0))/2;center[1]=np.concatenate(folded)[:,1].min()
    axis=np.array([[0,0,1],[0,1,0],[-1,0,0]],float)
    scene=np.eye(4);scene[:3,:3]=axis*.001;scene[:3,3]=-axis@center*.001
    result=defaultdict(list);wheels=[]
    for g in [i for i,x in enumerate(model['groups']) if not x['turret'] and i!=0]:
        members=[x for x in records if x['group']==g];tire=[x for x in members if len(x['path'])>1 and x['path'][1]==1]
        core=next(x for x in tire if 'HEXROLL' in x['name'])
        cv=(core['local'].min(0)+core['local'].max(0))/2
        wc=(scene@core['matrix']@np.append(cv,1))[:3]
        axle=normalized(axis@core['matrix'][:3,2]);axle*=1 if axle[0]>=0 else -1
        rv=Rotation.from_quat(model['groups'][g]['foldRotation']).as_rotvec();jointaxis=normalized(rv)
        pivot=np.array(model['groups'][g]['pivot'])
        # The inferred hinge is an axis line: its least-squares point has x=0.
        # Move along that same line to this module's axle plane, preserving
        # every source fold transform while giving each module its own origin.
        pivot+=jointaxis*np.dot(wc-pivot,jointaxis)
        index=len(wheels)
        coreworld=core['v']@scene[:3,:3].T+scene[:3,3]-wc
        radius=np.linalg.norm(coreworld-np.outer(coreworld@axle,axle),axis=1).max()
        wheels.append(dict(id=f'wheel-{index}',kind='hinge',sourceGroup=g,center=wc.tolist(),pivot=pivot.tolist(),axle=axle.tolist(),axis=jointaxis.tolist(),radius=float(radius),width=float(np.ptp(core['local'][:,2])*.001),foldAngle=float(np.linalg.norm(rv)*180/np.pi),minAngle=-4,maxAngle=float(np.linalg.norm(rv)*180/np.pi+4),moduleMass=1.8,rotorMass=.65,travelProvenance='hinge inferred from expanded/folded STEP poses; additional 4deg compliance is estimated'))
        for x in members:
            # Identify the wheel's actual nested assembly. Equal fold transforms
            # also group nearby actuator parts; they are not wheel rotors.
            rotating=tuple(x['path'][:2])==tuple(core['path'][:2]) and any(s in x['name'] for s in ['HEXROLL','麦轮钣金','TF01','TF04'])
            node='tire-'+str(index) if rotating else 'carrier-'+str(index)
            if '避震器' in x['name']:node='damper-'+str(index)
            result[node].append(x)
    result['frame']=[x for x in records if x['group']==0]
    for items in result.values():
        for x in items:x['v']=x['v']@scene[:3,:3].T+scene[:3,3];x['matrix']=scene@x['matrix']
    return result,wheels,scene,[]


def join(parts):
    vv=[];ff=[];cc=[];offset=0
    for v,t,c in parts:vv.append(v);ff.append(t+offset);cc.append(c);offset+=len(v)
    return np.concatenate(vv),np.concatenate(ff),np.concatenate(cc)


def hullmesh(points):
    directions=np.array([v for v in np.ndindex(3,3,3) if v!=(1,1,1)],float)-1
    directions/=np.linalg.norm(directions,axis=1)[:,None]
    # Use source vertices, rather than a circumscribed DOP: a coarse outer
    # envelope can protrude below the real arm and keep the tire off the floor.
    k=np.arange(88);z=1-2*(k+.5)/len(k);a=k*np.pi*(3-np.sqrt(5))
    directions=np.concatenate((directions,np.column_stack((np.sqrt(1-z*z)*np.cos(a),z,np.sqrt(1-z*z)*np.sin(a)))))
    vertices=np.unique(points[np.argmax(points@directions.T,axis=0)],axis=0)
    hull=ConvexHull(vertices);faces=hull.simplices.copy()
    for face,equation in zip(faces,hull.equations):
        a,b,c=vertices[face]
        if np.dot(np.cross(b-a,c-a),equation[:3])<0:face[1],face[2]=face[2],face[1]
    assert len(faces)<255 and hull.volume>1e-12
    return vertices,faces


def export(name):
    model=json.loads((DATA/f'assets/robots/{name}/model.json').read_text())
    parts,wheels,scene,missing=(source_sweeper if name=='sweeper' else source_jiageng)(model)
    target=DATA/f'assets/developer/robots/{name}';target.mkdir(parents=True,exist_ok=True)
    nodes=[];assignment=[];total=sum(len(x['t']) for items in parts.values() for x in items)
    for key,items in parts.items():
        index=int(key.split('-')[-1]) if key!='frame' else -1
        pivot=np.zeros(3) if index<0 else np.array(wheels[index]['center' if key.startswith('tire') else 'pivot'])
        v,t,c=join([(x['v']-pivot,x['t'],x['c']) for x in items]);paths=[];stats=[]
        for lod,budget in enumerate([65000,18000,4000]):
            nv,nt,nc=simplify_painted(v,t,c,max(16,round(budget*len(t)/total)))
            path=target/f'{key}-{lod}.tcrmesh';stats.append(write_mesh(path,nv,nt,nc));paths.append(str(path.relative_to(DATA)))
        proxies=[]
        if not key.startswith(('tire','damper')):
            # Carrier assemblies surround the tire and rail. One convex hull
            # fills their gaps and can falsely wedge a wheel at a ramp seam.
            clusters=[x['v']-pivot for x in items] if key.startswith('carrier-') else [v]
            if key=='frame':
                mid=(v.min(0)+v.max(0))/2
                if name=='sweeper':
                    # A quadrant hull bridges the low battery/rail mounts to
                    # distant frame corners, filling empty space under the car.
                    # Preserve each low source component as a separate convex
                    # proxy; only aggregate parts fully above the underbody.
                    low=[x['v']-pivot for x in items if x['v'][:,1].min()<.065]
                    high=np.concatenate([x['v']-pivot for x in items if x['v'][:,1].min()>=.065])
                    clusters=low+[high[((high[:,0]>=mid[0])==bool(a))&((high[:,2]>=mid[2])==bool(b))] for a in [0,1] for b in [0,1]]
                else:
                    clusters=[v[((v[:,0]>=mid[0])==bool(a))&((v[:,2]>=mid[2])==bool(b))] for a in [0,1] for b in [0,1]]
            for i,points in enumerate(clusters):
                if len(points)<4:continue
                cv,ct=hullmesh(points);p=target/f'{key}-collision-{i}.tcrmesh';write_mesh(p,cv,ct);proxies.append(str(p.relative_to(DATA)))
        nodes.append(dict(id=key,module=index,pivot=pivot.tolist(),meshes=paths,collisionMeshes=proxies,lod=stats,sourceTriangles=len(t)))
        assignment.extend(dict(path=x['path'],name=x['name'],node=key,triangles=len(x['t'])) for x in items)
        if key.startswith('damper'):
            w=wheels[index];pv=np.array(w['pivot']);pts=v+pivot;ax=normalized(items[0]['matrix'][:3,1]);projection=pts@ax
            lo,hi=projection.min()+.004,projection.max()-.004
            transverse=pts.mean(0)-ax*np.dot(pts.mean(0),ax);ends=[transverse+ax*lo,transverse+ax*hi]
            ends.sort(key=lambda e:np.linalg.norm(e-pv));w['damperFixed']=ends[0].tolist();w['damperMoving']=ends[1].tolist()
        print(name,key,len(t),'->',[s['triangles'] for s in stats],flush=True)
    for w in wheels:
        p=np.array(w['center']);w['label']=('F' if p[2]>=0 else 'R')+('L' if p[0]<0 else 'R')
        rolling=normalized(np.cross(w['axle'],[0,1,0]));w['rollingDirection']=rolling.tolist()
        traction=rolling if name=='sweeper' else normalized([-np.sign(p[0]*p[2]),0,1])
        w['tractionDirection']=traction.tolist()
        assert abs(np.dot(rolling,traction))>.5
    rig=dict(schema=1,id=name,units='m kg s degrees',sourceSha256=model['sourceSha256'],sourceMapping=scene.tolist(),parameterProvenance='geometry from source CAD; masses, compliance and motor limits are tunable estimates',defaultFold=model['defaultFold'],mass=18,bodyMass=18-sum(w['moduleMass'] for w in wheels),nodes=nodes,wheels=wheels,legacyTurretGroups=[i for i,g in enumerate(model['groups']) if g['turret']],missingSourceComponents=missing)
    matrix=np.array([[*np.array(w['tractionDirection'])[[0,2]],np.dot(w['tractionDirection'],np.cross([0,1,0],w['center']))] for w in wheels])
    assert np.linalg.matrix_rank(matrix)==3, matrix
    rig['wheelMatrixCondition']=float(np.linalg.cond(matrix));(target/'rig.json').write_text(json.dumps(rig,ensure_ascii=False,indent=2)+'\n')
    OUT.mkdir(parents=True,exist_ok=True);(OUT/f'{name}-assignment.json').write_text(json.dumps(assignment,ensure_ascii=False,indent=2)+'\n')
    (OUT/f'{name}-calibration.json').write_text(json.dumps(rig,ensure_ascii=False,indent=2)+'\n')
    print('VEHICLE_RIG_EXPORTED',name,len(nodes),'nodes',len(wheels),'wheels',total,'source triangles',flush=True)


if __name__=='__main__':
    for name in ['sweeper','jiageng']:export(name)
