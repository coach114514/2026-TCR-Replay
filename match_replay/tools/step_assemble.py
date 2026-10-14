"""Package converted official CAD components into shared Unity runtime meshes."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import struct
import shutil
import numpy as np
from match_replay.tools.step_index import StepIndex
from match_replay.tools.step_convert import placement

def simplify_colored(vertices,triangles,colors,terrain=False):
    """Simplify each paint group independently so small LEDs keep their color."""
    if terrain:return vertices,triangles,colors,{'simplificationApplied':False}
    import fast_simplification
    palette,groups=np.unique(colors[triangles[:,0]],axis=0,return_inverse=True)
    if not np.all(colors[triangles]==colors[triangles[:,0]][:,None,:]):raise ValueError('Paint changes within a source triangle')
    parts=[];faces=[];paint=[];offset=0;errors=[];rejected=[]
    ratio=min(.25,80_000/len(triangles))
    for group,color in enumerate(palette):
        indices=triangles[groups==group]
        points,inverse=np.unique(vertices[indices.flatten()],axis=0,return_inverse=True)
        indices=inverse.reshape((-1,3))
        # Small labels and light strips retain their original topology.
        if len(indices)>10_000:
            simplified,result=fast_simplification.simplify(points,indices,target_count=max(1000,int(len(indices)*ratio)),agg=5)
            extent=max(np.max(np.abs(simplified.min(axis=0)-points.min(axis=0))),np.max(np.abs(simplified.max(axis=0)-points.max(axis=0))))
            if len(result)>0 and np.isfinite(simplified).all() and extent<=10:
                points,indices=simplified,result;errors.append(float(extent))
            else:rejected.append(float(extent))
        parts.append(points);faces.append(indices+offset);paint.append(np.tile(color,(len(points),1)));offset+=len(points)
    return np.concatenate(parts),np.concatenate(faces),np.concatenate(paint),{'simplificationApplied':len(errors)>0,'simplificationBoundsErrorMm':max(errors,default=0),'rejectedColorGroupBoundsErrorsMm':rejected,'paintGroups':len(palette)}

def main():
    p=argparse.ArgumentParser()
    p.add_argument('--source',type=Path,default=Path('RMUC2026_V2.0.0.stp'))
    p.add_argument('--cache',type=Path,default=Path.home()/'.cache/tcr-step-index')
    p.add_argument('--output',type=Path,default=Path('match_replay/unity/Assets/StreamingAssets/replay'))
    p.add_argument('--colored',action='store_true',help='Use restored source surface styles and preserve paint boundaries')
    args=p.parse_args()
    idx=StepIndex(args.source,args.cache);names,relations=idx.assembly()
    top=[(eid,r) for eid,r in relations.items() if r['parent']==7011251]
    component_dir=args.cache/'components';dest=args.output/'assets/official_final';dest.mkdir(parents=True,exist_ok=True)
    # Source CAD X/Y lie in the field plane; Z is vertical. The floor's datum
    # and centreline are read from the official assembly, preserving its slopes.
    swap=np.array([[1,0,0],[0,0,1],[0,1,0]],dtype=float)
    world=np.eye(4);world[:3,:3]=swap*.001;world[:3,3]=[14,1.941343676495,5.875656323505]
    local=np.eye(4);local[:3,:3]=swap*.001
    meshes={};reports=[];total_triangles=0
    for definition in sorted({r['child'] for _,r in top}):
        data=np.load(component_dir/f'{definition}{"-colored" if args.colored else ""}.npz');vertices=data['positions'];triangles=data['indices']
        report=json.loads((component_dir/f'{definition}.json').read_text())
        if report.get('extractorVersion')!=2: raise ValueError('Incomplete alternate representation extraction')
        original=len(triangles)
        report['simplificationApplied']=False
        colors=None
        if args.colored:
            color_report=json.loads((component_dir/f'{definition}-colored.json').read_text())
            if color_report.get('colorPipelineVersion')!=2 or color_report['source']!=json.loads((args.cache/'stamp.json').read_text()):raise ValueError('CAD color cache mismatch')
            report.update({k:color_report[k] for k in ['coloredFaces','sourceSurfaceStyles','colorPipelineVersion']})
            vertices,triangles,colors,quality=simplify_colored(vertices,triangles,data['colors'],definition==343127)
            report.update(quality)
        # The field terrain remains unchanged. Decorative detail in repeated
        # mechanisms is reduced, keeping their extents within a 10mm budget.
        if not args.colored and definition!=343127 and len(triangles)>80_000:
            import fast_simplification
            welded,inverse=np.unique(vertices,axis=0,return_inverse=True)
            target=min(80_000,max(25_000,len(triangles)//4))
            simplified,faces=fast_simplification.simplify(welded,inverse[triangles],target_count=target,agg=5)
            if len(faces)==0 or not np.isfinite(simplified).all(): raise ValueError('Invalid simplified mesh')
            extent_error=max(np.max(np.abs(simplified.min(axis=0)-vertices.min(axis=0))),np.max(np.abs(simplified.max(axis=0)-vertices.max(axis=0))))
            if extent_error<=10:
                vertices,triangles=simplified,faces
                report['simplificationApplied']=True
                report['simplificationBoundsErrorMm']=float(extent_error)
            else: report['rejectedSimplificationBoundsErrorMm']=float(extent_error)
        vertices=(vertices@swap.T*.001).astype('<f4')
        triangles=triangles[:,[0,2,1]].astype('<u4')
        if not np.isfinite(vertices).all() or triangles.max()>=len(vertices): raise ValueError('Invalid mesh coordinates or indices')
        name=f'{definition}.tcrmesh'
        with (dest/name).open('wb') as f:
            f.write(struct.pack('<8sII',b'TCRMESH2' if colors is not None else b'TCRMESH1',len(vertices),triangles.size))
            f.write(vertices.tobytes());f.write(triangles.tobytes())
            if colors is not None:
                rgba=np.column_stack((colors,np.full(len(colors),255))).astype('u1');f.write(rgba.tobytes())
        meshes[definition]='assets/official_final/'+name
        report.update({'originalTriangles':original,'displayTriangles':len(triangles),'displayVertices':len(vertices),'meshSha256':hashlib.sha256((dest/name).read_bytes()).hexdigest()})
        reports.append(report)
        print('PACKAGED',definition,original,'→',len(triangles),flush=True)
    region=json.loads(Path('match_replay/field_configs/region_2026.json').read_text())
    config=dict(region);config.update({'id':'rmuc_2026_final_official','name':'2026 全国赛 · 官方 STEP 模型','meshFormat':'assembly_binary_v1','mesh':'assets/official_final/assembly.json','meshFloorHeight':0,'calibration':'cad_frame_verified_replay_alignment_provisional','notes':'几何来自官方全国赛STEP装配。毫米转米、保留原装配变换；场地中心线与地面基准取自CAD。区域赛数据在此仅作显式对照；轨迹尚未与全国赛日志完成校准。'})
    config['coordinateConvention']='单位米；场地左下为(0,0)，x向右、y向上；CAD(X,Y,Z)映射为Unity(X,Z,Y)，采用原装配变换；CAD平面中心线Y=1624.343676495mm，地面Z=-1941.343676495mm。'
    config['landmarkCalibration']='基地、前哨站、资源站及飞镖发射站取CAD装配原点；其余地物标签沿用规则图示区域点，尚非逐构件精确绑定。'
    if args.colored:
        appearance_dir=args.output/'assets/appearance';appearance_dir.mkdir(parents=True,exist_ok=True)
        for name in ['rules_2026_final_top.jpg','powder_coat.png']:
            shutil.copy2(Path('match_replay/art_assets/textures')/name,appearance_dir/name)
        config['texture']='assets/appearance/rules_2026_final_top.jpg'
        config['textureScaleOffset']=[1480/1683,791/938,98/1683,80/938]
        config['surfaceTexture']='assets/appearance/powder_coat.png'
        config['textureSource']='规则V2.0.1图4-1原始俯视渲染图；按28×15米内场边界投影，红方左侧、蓝方右侧。'
    bindings={5891081:'base',3648370:'outpost',1402169:'resource_station',6919696:'dart_station'}
    parts=[]
    for eid,r in top:
        definition=r['child'];matrix=world@placement(idx,r)@np.linalg.inv(local)
        if not np.allclose(matrix[:3,:3].T@matrix[:3,:3],np.eye(3),atol=1e-6): raise ValueError('Non-rigid assembly placement')
        team='red' if matrix[0,3]<14 else 'blue';role=bindings.get(definition)
        binding=f'{team}_{role}' if role else ''
        part_id=f'cad_{definition}_{eid}'
        color=[.28,.33,.39]
        if role in ('base','outpost'): color=[.58,.22,.24] if team=='red' else [.18,.42,.64]
        part={'id':part_id,'name':binding or r['name'],'sourceProduct':r['name'],'mesh':meshes[definition],'matrix':matrix.flatten().tolist(),'binding':binding,'walkable':definition==343127,'textured':definition==343127,'color':color}
        part['colorMode']='structural' if definition in (343127,181869,196117,41070,56906,7011105) else 'source_cad'
        part['team']=team if role else 'neutral'
        parts.append(part)
        if role in ('base','outpost'):
            anchor=next(a for a in config['buildings'] if a['team']==team and a['role']==role)
            anchor.update({'x':float(matrix[0,3]),'y':float(matrix[2,3]),'z':float(matrix[1,3]),'modelNode':part_id,'source':f'官方STEP产品定义#{definition}，装配关系#{eid}；CAD装配原点'})
        elif role:
            marker=next(a for a in config['landmarks'] if a['team']==team and a['role']==role)
            marker.update({'x':float(matrix[0,3]),'y':float(matrix[2,3]),'z':float(matrix[1,3]),'modelNode':part_id,'source':f'官方STEP装配关系#{eid}；CAD装配原点'})
        total_triangles+=next(a['displayTriangles'] for a in reports if a['definition']==definition)
    with args.source.open('rb') as source: source_hash=hashlib.file_digest(source,'sha256').hexdigest()
    manifest={'schema':1,'sourceFile':args.source.name,'sourceSha256':source_hash,'sourceUnits':'mm','cadToSceneMatrix':world.flatten().tolist(),'parts':parts,'components':reports,'displayTrianglesIncludingInstances':total_triangles}
    (dest/'assembly.json').write_text(json.dumps(manifest,ensure_ascii=False,separators=(',',':'))+'\n')
    final_dir=args.output/'fields';final_dir.mkdir(exist_ok=True)
    (final_dir/'final_2026.json').write_text(json.dumps(config,ensure_ascii=False,indent=2)+'\n')
    Path('match_replay/field_configs/final_2026.json').write_text(json.dumps(config,ensure_ascii=False,indent=2)+'\n')
    catalog={'schema':1,'defaultField':config['id'],'fields':[{'id':config['id'],'name':'官方全国赛模型','file':'fields/final_2026.json'},{'id':region['id'],'name':'原区域赛模型','file':'field_config.json'}]}
    (args.output/'fields_catalog.json').write_text(json.dumps(catalog,ensure_ascii=False)+'\n')
    print(f'ASSEMBLY_OK uniqueMeshes={len(meshes)} instances={len(parts)} triangles={total_triangles}',flush=True)

if __name__=='__main__':main()
