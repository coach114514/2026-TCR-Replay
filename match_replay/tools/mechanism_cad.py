"""Inspect original solid boundaries before extracting moving CAD components."""
from pathlib import Path
import argparse
import json
import numpy as np
from .step_convert import read
from .step_colors import collect_colors
from .step_assemble import simplify_colored
import struct

CACHE=Path.home()/'.cache/tcr-step-index/components'

def inspect(definition):
    from OCP.XCAFDoc import XCAFDoc_ShapeTool
    from OCP.TopExp import TopExp_Explorer
    from OCP.TopAbs import TopAbs_SOLID,TopAbs_FACE,TopAbs_FORWARD
    from OCP.Bnd import Bnd_Box
    from OCP.BRepBndLib import BRepBndLib
    from OCP.BRepGProp import BRepGProp
    from OCP.GProp import GProp_GProps
    document,tool,label=read(CACHE/f'{definition}-colored.step')
    shape=XCAFDoc_ShapeTool.GetShape_s(label);styles=collect_colors(label)
    solids=[];exp=TopExp_Explorer(shape,TopAbs_SOLID)
    while exp.More():
        solid=exp.Current();box=Bnd_Box();BRepBndLib.Add_s(solid,box)
        props=GProp_GProps();BRepGProp.VolumeProperties_s(solid,props)
        c=props.CentreOfMass();colors={};faces=0;f=TopExp_Explorer(solid,TopAbs_FACE)
        while f.More():
            color=styles.get(f.Current().Oriented(TopAbs_FORWARD))
            if color is not None:
                key=','.join(str(int(v)) for v in color);colors[key]=colors.get(key,0)+1
            faces+=1;f.Next()
        solids.append(dict(index=len(solids),bounds=list(box.Get()),center=[c.X(),c.Y(),c.Z()],volume=props.Mass(),colors=colors,faces=faces))
        exp.Next()
    path=CACHE/f'{definition}-solids.json';path.write_text(json.dumps(dict(definition=definition,solids=solids),indent=2))
    print(f'MECHANISM_SOLIDS_OK definition={definition} solids={len(solids)} file={path}',flush=True)
    return document,tool,label,shape,styles,solids

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--inspect',type=int,nargs='+')
    parser.add_argument('--export',action='store_true')
    args=parser.parse_args()
    if args.export:export()
    else:
        for definition in args.inspect or [3648370,2702539]:inspect(definition)

def grouped_mesh(shape,styles,shells=None):
    from OCP.TopTools import TopTools_IndexedMapOfShape
    from OCP.TopExp import TopExp_Explorer
    from OCP.TopAbs import TopAbs_SOLID,TopAbs_SHELL,TopAbs_FACE,TopAbs_FORWARD,TopAbs_REVERSED
    from OCP.TopoDS import TopoDS
    from OCP.BRepMesh import BRepMesh_IncrementalMesh
    from OCP.BRep import BRep_Tool
    from OCP.TopLoc import TopLoc_Location
    face_map=TopTools_IndexedMapOfShape();owner=[-1];si=0;solids=TopExp_Explorer(shape,TopAbs_SOLID)
    while solids.More():
        exp=TopExp_Explorer(solids.Current(),TopAbs_FACE)
        while exp.More():
            index=face_map.Add(exp.Current().Oriented(TopAbs_FORWARD))
            if index==len(owner):owner.append(si)
            exp.Next()
        si+=1;solids.Next()
    if shells is not None:
        # Open/non-solid shells are real source geometry as well. Previously
        # these faces all received owner -1 and stayed in the stationary mesh,
        # leaving a complete grey arm housing behind when the solid LEDs moved.
        from OCP.Bnd import Bnd_Box
        from OCP.BRepBndLib import BRepBndLib
        explorer=TopExp_Explorer(shape,TopAbs_SHELL)
        while explorer.More():
            shell=explorer.Current();faces=TopExp_Explorer(shell,TopAbs_FACE);free=0;total=0
            shell_id=len(shells)
            while faces.More():
                face=faces.Current().Oriented(TopAbs_FORWARD);index=face_map.FindIndex(face);total+=1
                if index==0 or owner[index]<0:
                    free+=1;index=face_map.Add(face)
                    if index==len(owner):owner.append(-2-shell_id)
                faces.Next()
            if free:
                box=Bnd_Box();BRepBndLib.Add_s(shell,box)
                shells.append(dict(index=shell_id,faces=total,freeFaces=free,bounds=list(box.Get())))
            explorer.Next()
    BRepMesh_IncrementalMesh(shape,5,False,.5,True).Perform()
    vertices=[];triangles=[];paint=[];groups=[];exp=TopExp_Explorer(shape,TopAbs_FACE)
    while exp.More():
        face=TopoDS.Face_s(exp.Current());loc=TopLoc_Location();tri=BRep_Tool.Triangulation_s(face,loc)
        if tri is None:
            BRepMesh_IncrementalMesh(face,.25,False,.2,False).Perform();tri=BRep_Tool.Triangulation_s(face,loc)
        if tri is None:exp.Next();continue
        color=styles.get(face.Oriented(TopAbs_FORWARD));color=(100,100,100) if color is None else color
        solid=owner[face_map.FindIndex(face.Oriented(TopAbs_FORWARD))]
        offset=len(vertices);transform=loc.Transformation()
        for i in range(1,tri.NbNodes()+1):
            p=tri.Node(i).Transformed(transform);vertices.append((p.X(),p.Y(),p.Z()));paint.append(color)
        for i in range(1,tri.NbTriangles()+1):
            a,b,c=tri.Triangle(i).Get()
            triangles.append((offset+a-1,offset+c-1,offset+b-1) if face.Orientation()==TopAbs_REVERSED else (offset+a-1,offset+b-1,offset+c-1))
            groups.append(solid)
        exp.Next()
    return np.asarray(vertices,dtype=np.float32),np.asarray(triangles,dtype=np.int32),np.asarray(paint,dtype=np.uint8),np.asarray(groups,dtype=np.int16)

def write_mesh(path,vertices,triangles,colors):
    used,inverse=np.unique(triangles.flatten(),return_inverse=True)
    vertices,triangles,colors=vertices[used],inverse.reshape((-1,3)),colors[used]
    vertices,triangles,colors,_=simplify_colored(vertices,triangles,colors)
    vertices=vertices[:,[0,2,1]]*.001;triangles=triangles[:,[0,2,1]]
    path.parent.mkdir(parents=True,exist_ok=True)
    with path.open('wb') as f:
        f.write(struct.pack('<8sII',b'TCRMESH2',len(vertices),triangles.size))
        f.write(vertices.astype('<f4').tobytes());f.write(triangles.astype('<u4').tobytes())
        f.write(np.column_stack((colors,np.full(len(colors),255))).astype('u1').tobytes())
    return {'vertices':len(vertices),'triangles':len(triangles),'bounds':[vertices.min(axis=0).tolist(),vertices.max(axis=0).tolist()]}

def vector(point):return np.asarray(point)[[0,2,1]]*.001

def export():
    destination=Path(__file__).resolve().parents[1]/'unity/Assets/StreamingAssets/replay'
    report=[];config={'schema':1,'geometrySplitVersion':4,'source':'official_STEP_solid_and_open_shell_boundaries','outpost':{},'rune':{},'baseGuide':base_guide_geometry()}
    for definition in (3648370,2702539):
        include_shells=True
        cache=CACHE/f'{definition}-grouped{"-v2" if include_shells else ""}.npz'
        shell_cache=CACHE/f'{definition}-grouped-shells-v2.json'
        if not cache.exists():
            doc,tool,label,shape,styles,solids=inspect(definition)
            shells=[]
            vertices,triangles,colors,groups=grouped_mesh(shape,styles,shells if include_shells else None)
            np.savez_compressed(cache,positions=vertices,indices=triangles,colors=colors,groups=groups)
            if include_shells:shell_cache.write_text(json.dumps(shells,indent=2))
        else:
            data=np.load(cache);vertices,triangles,colors,groups=(data[k] for k in ('positions','indices','colors','groups'))
            solids=json.loads((CACHE/f'{definition}-solids.json').read_text())['solids']
            shells=json.loads(shell_cache.read_text()) if include_shells else []
        if definition==3648370:
            # Solid7 is the common three-arm frame, 8..25 are three repeated
            # armor/connector groups. Solid26 is the upper fixed dart module.
            moving=list(range(7,26));center=np.mean([solids[i]['center'] for i in (11,17,23)],axis=0)
            centers=np.asarray([solids[i]['center'] for i in (11,17,23)])
            # Source CAD Y is vertical. The plates intentionally have different
            # Y heights: their three centres do NOT define the rotation plane.
            axis=np.array([0.,1.,0.])
            normals=centers-center;normals[:,1]=0;normals/=np.linalg.norm(normals,axis=1)[:,None]
            normals=normals*np.cos(np.deg2rad(15))+axis*np.sin(np.deg2rad(15))
            config['outpost'].update(pivot=vector(center).tolist(),axis=axis[[0,2,1]].tolist(),
                panels=[dict(center=vector(p).tolist(),normal=n[[0,2,1]].tolist(),half=[.0445,.0445,.008]) for p,n in zip(centers,normals)],
                solidGroups=moving,rotationSpace='world_vertical',geometryNote='保留三套89mm装甲中心的高度差，绕世界竖直轴水平旋转；装甲面保留规则75°安装倾角，禁止通过不同高度中心拟合斜轴')
            # Free shells91..112 are rotor fasteners;113..184,185..247,
            # 248..267 belong to the three source armor casings. Shell268 starts
            # the stationary upper dart module, not another armor segment.
            if len(shells)!=393 or shells[113]['freeFaces']!=383 or shells[185]['freeFaces']!=383 or shells[248]['freeFaces']!=688 or np.any(groups==-1):
                raise RuntimeError('Outpost source shell boundaries changed; re-audit source CAD')
            moving_shells=list(range(91,268));shell_owners=[-2-i for i in moving_shells]
            config['outpost']['shellPartition']=dict(freeShellCount=len(shells),movingFreeShellCount=len(moving_shells),unassignedTriangles=0,
                movingShellGroups=moving_shells,fixedShellGroups=[i for i in range(len(shells)) if i not in moving_shells],
                movingShellTriangles=int(np.sum(np.isin(groups,shell_owners))),sourceTriangles=len(triangles))
            splits={'fixed':~np.isin(groups,[*moving,*shell_owners]),'rotor':np.isin(groups,[*moving,*shell_owners])}
        else:
            # Each source arm comprises ten solids AND 95 free shells, including
            # its grey housing and hardware. Keep source motors, crossbeam and
            # upright supports fixed; move the whole arm, not just its LEDs.
            if len(shells)!=1063 or np.any(groups==-1):
                raise RuntimeError('Rune source shell layout changed or contains unassigned faces; re-audit CAD')
            moving_shells=[*range(110,585),*range(587,1062)]
            shell_owners=[-2-i for i in moving_shells]
            # Keep both R plates, centre covers and screws in their native
            # upright pose, per the user's requested stationary centre. The
            # handbook does not specify the R mark's own motion separately.
            hub_solids=[*range(14,32),*range(33,51)]
            moving=list(range(58,158))
            center=(np.asarray(solids[30]['center'])+np.asarray(solids[49]['center']))*.5
            axis=np.asarray(solids[30]['center'])-np.asarray(solids[49]['center']);axis/=np.linalg.norm(axis)
            # The source paints both centre R marks red. Solid30 is on the
            # positive axis (blue face); recolor only its red paint vertices.
            blue_hub=np.unique(triangles[groups==30].flatten());colors=colors.copy()
            red_paint=(colors[blue_hub,0]>75)&(colors[blue_hub,1]<30)&(colors[blue_hub,2]<30)
            colors[blue_hub[red_paint]]=[6,33,255]
            anchors=[]
            for team,owner,sign in [('red',49,-1),('blue',30,1)]:
                bounds=np.asarray(solids[owner]['bounds']);mark=(bounds[:3]+bounds[3:])*.5
                anchors.append(dict(team=team,center=vector(mark+axis*sign*1).tolist(),normal=(axis*sign)[[0,2,1]].tolist()))
            config['rune'].update(pivot=vector(center).tolist(),axis=axis[[0,2,1]].tolist(),solidGroups=moving,hubSolidGroups=hub_solids,hubMotion='fixed_upright',selectionAnchors=anchors,arms=[],
                shellPartition=dict(freeShellCount=len(shells),movingFreeShellCount=len(moving_shells),unassignedTriangles=int(np.sum(groups==-1)),
                    fixedShellGroups=[i for i in range(len(shells)) if i not in moving_shells],
                    movingShellTriangles=int(np.sum(np.isin(groups,shell_owners))),
                    sourceTriangles=len(triangles),method='OCCT solid faces plus source free-shell ownership; fixed/rotating exhaustive partition'))
            splits={'fixed':~np.isin(groups,[*moving,*hub_solids,*shell_owners]),'hub':np.isin(groups,hub_solids)}
            for team,start,shell_start in (('red',58,110),('blue',108,587)):
                for i in range(5):
                    name=f'{team}_arm_{i}';ids=list(range(start+i*10,start+(i+1)*10))
                    arm_shells=list(range(shell_start+i*95,shell_start+(i+1)*95))
                    # Audit against that arm's source housing, never a height
                    # cut: the first free shell is the complete grey casing.
                    housing=np.asarray(shells[arm_shells[0]]['bounds'])
                    if shells[arm_shells[0]]['freeFaces']!=453:
                        raise RuntimeError(f'Rune arm housing boundary changed: {name}')
                    solid_center=np.asarray(solids[ids[0]]['center'])
                    if np.any(solid_center<housing[:3]-5) or np.any(solid_center>housing[3:]+5):
                        raise RuntimeError(f'Rune shell group does not correspond to colored arm: {name}')
                    splits[name]=np.isin(groups,[*ids,*[-2-j for j in arm_shells]])
                    points=np.unique(vertices[triangles[groups==start+i*10+6].flatten()],axis=0)
                    normal=axis*(-1 if team=='red' else 1)
                    target=(points.min(axis=0)+points.max(axis=0))*.5
                    target+=normal*np.dot(points.mean(axis=0)-target,normal)
                    config['rune']['arms'].append(dict(name=name,team=team,index=i,solidGroups=ids,shellGroups=arm_shells,
                        targetCenter=vector(target).tolist(),targetNormal=normal[[0,2,1]].tolist(),
                        targetRadius=.15,targetNote='靶面中心取对应黑色背板实体投影包围框，300mm有效圆面为规则碰撞代理'))
        for name,mask in splits.items():
            if not mask.any():raise RuntimeError(f'Empty CAD group {definition}/{name}')
            filename=f'assets/mechanisms/{definition}_{name}.tcrmesh'
            details=write_mesh(destination/filename,vertices,triangles[mask],colors)
            report.append(dict(definition=definition,group=name,file=filename,**details))
            cfg=config['outpost' if definition==3648370 else 'rune']
            if name in ('fixed','rotor','hub'):cfg[name]=filename
            else:next(a for a in cfg['arms'] if a['name']==name)['mesh']=filename
        coverage=np.sum(np.stack(list(splits.values())),axis=0)
        if np.any(coverage!=1):raise RuntimeError('CAD split omitted or duplicated source triangles')
        print(f'MECHANISM_EXPORT_OK definition={definition} sourceTriangles={len(triangles)} movingSolids={len(moving)}',flush=True)
    config['geometryReports']=report
    (destination/'mechanisms_config.json').write_text(json.dumps(config,ensure_ascii=False,indent=2))
    artifact=Path(__file__).resolve().parents[1]/'artifacts/mechanisms/cad-split-report.json'
    artifact.write_text(json.dumps(config,ensure_ascii=False,indent=2))


def base_guide_geometry():
    # Exact OCP circular edges of the upper movable dart module, also identified
    # in rule figs4-11/4-12 and the user's bezel close-up. The lower Y=942mm
    # sloping panel is not the emitter. The native reserved aperture is60mm;
    # retain its surrounding80mm bezel and fit the rule's55mm luminous face.
    normal=np.array([0.,.2504,.9681]);normal/=np.linalg.norm(normal)
    center=np.array([.0044,1042.3819,88.5184])+normal*1.5
    return dict(center=vector(center).tolist(),normal=normal[[0,2,1]].tolist(),diameter=.055,
                evidence='规则图4-11/4-12/4-13和用户图1：上部飞镖活动模块圆形预留；OCP原生圆边中心(0.0044,1042.3819,88.5184)mm、半径30mm、轴(0,0.2504,0.9681)；55mm发光面向外1.5mm，保留源半圆包裹及外框')

if __name__=='__main__':main()
