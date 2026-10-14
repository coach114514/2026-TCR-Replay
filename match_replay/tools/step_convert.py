"""Convert independently closed STEP subassemblies using OpenCascade/OCP."""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import re
import time
import numpy as np
from match_replay.tools.step_index import StepIndex

def axis(index,eid):
    refs=index.refs(eid)
    def vector(ref):
        record=index.record(ref).decode()
        raw=record[record.rfind('(')+1:record.rfind(')')].rstrip(')')
        return np.array([float(n) for n in raw.split(',')])
    origin=vector(refs[0]); z=vector(refs[1]); x=vector(refs[2])
    z/=np.linalg.norm(z); x-=z*np.dot(x,z); x/=np.linalg.norm(x); y=np.cross(z,x)
    matrix=np.eye(4);matrix[:3,:3]=np.column_stack((x,y,z));matrix[:3,3]=origin
    return matrix

def placement(index,relationship):
    a,b=index.refs(relationship['transform'])
    return axis(index,b)@np.linalg.inv(axis(index,a))

def extract(index,definition,destination):
    names,relationships=index.assembly()
    definitions={definition}; pending=[definition]
    while pending:
        parent=pending.pop()
        for relation in relationships.values():
            if relation['parent']==parent and relation['child'] not in definitions:
                definitions.add(relation['child']);pending.append(relation['child'])
    roots=[];representations=set()
    for eid,record in index.meta.items():
        if '=SHAPE_DEFINITION_REPRESENTATION(' in record:
            pds,_=index.refs(eid)
            if index.refs(pds)[0] in definitions: roots.append(eid);representations.add(index.refs(eid)[1])
    roots.extend(r['context'] for r in relationships.values() if r['parent'] in definitions)
    # AP203 can attach the actual BRep through an alternate representation,
    # while the product's direct SHAPE_REPRESENTATION contains only its axes.
    changed=True
    while changed:
        changed=False
        for eid,record in index.meta.items():
            if '=SHAPE_REPRESENTATION_RELATIONSHIP(' not in record: continue
            refs=index.refs(eid)
            if any(ref in representations for ref in refs) and eid not in roots:
                roots.append(eid);representations.update(refs);changed=True
    count=index.extract(roots,destination)
    print(f'Extracted {names[definition]}: {count:,} entities, {destination.stat().st_size/1e6:.1f} MB',flush=True)

def read(path):
    from OCP.STEPCAFControl import STEPCAFControl_Reader
    from OCP.TDocStd import TDocStd_Document
    from OCP.TCollection import TCollection_ExtendedString
    from OCP.XCAFDoc import XCAFDoc_DocumentTool
    from OCP.TDF import TDF_LabelSequence
    from OCP.IFSelect import IFSelect_RetDone
    reader=STEPCAFControl_Reader()
    document=TDocStd_Document(TCollection_ExtendedString('TCR'))
    reader.SetNameMode(True);reader.SetColorMode(True)
    print('Reading',path.name,flush=True)
    if reader.ReadFile(str(path))!=IFSelect_RetDone: raise RuntimeError('STEP read failed')
    print('Transferring',path.name,flush=True)
    if not reader.Transfer(document): raise RuntimeError('STEP transfer failed')
    tool=XCAFDoc_DocumentTool.ShapeTool_s(document.Main())
    roots=TDF_LabelSequence(); tool.GetFreeShapes(roots)
    if roots.Length()!=1: raise RuntimeError(f'Expected one assembly root, got {roots.Length()}')
    return document,tool,roots.Value(1)

def tessellate(shape,linear=5,angular=.5,styles=None):
    from OCP.BRepMesh import BRepMesh_IncrementalMesh
    from OCP.TopExp import TopExp_Explorer
    from OCP.TopAbs import TopAbs_FACE,TopAbs_REVERSED,TopAbs_FORWARD
    from OCP.TopoDS import TopoDS
    from OCP.BRep import BRep_Tool
    from OCP.TopLoc import TopLoc_Location
    from OCP.Bnd import Bnd_Box
    from OCP.BRepBndLib import BRepBndLib
    start=time.monotonic()
    BRepMesh_IncrementalMesh(shape,linear,False,angular,True).Perform()
    from OCP.BRepGProp import BRepGProp
    from OCP.GProp import GProp_GProps
    positions=[];indices=[];vertex_colors=[];colored_faces=0;faces=0;missing=0;missing_area=0;total_area=0
    explorer=TopExp_Explorer(shape,TopAbs_FACE)
    while explorer.More():
        face=TopoDS.Face_s(explorer.Current());loc=TopLoc_Location()
        props=GProp_GProps();BRepGProp.SurfaceProperties_s(face,props); area=props.Mass();total_area+=area
        tri=BRep_Tool.Triangulation_s(face,loc)
        if tri is None:
            BRepMesh_IncrementalMesh(face,min(linear,.25),False,.2,False).Perform()
            tri=BRep_Tool.Triangulation_s(face,loc)
        if tri is None:
            missing+=1;missing_area+=area;explorer.Next();continue
        offset=len(positions);transform=loc.Transformation()
        face_color=styles.get(face.Oriented(TopAbs_FORWARD)) if styles is not None else None
        if face_color is not None: colored_faces+=1
        for i in range(1,tri.NbNodes()+1):
            p=tri.Node(i).Transformed(transform);positions.append((p.X(),p.Y(),p.Z()))
            if styles is not None:vertex_colors.append(face_color if face_color is not None else (100,100,100))
        reverse=face.Orientation()==TopAbs_REVERSED
        for i in range(1,tri.NbTriangles()+1):
            a,b,c=tri.Triangle(i).Get()
            indices.append((offset+a-1,offset+c-1,offset+b-1) if reverse else (offset+a-1,offset+b-1,offset+c-1))
        faces+=1;explorer.Next()
    if not positions or missing_area>max(1,total_area*1e-6): raise RuntimeError(f'Triangulation incomplete: faces={faces}, missing={missing}, missingAreaMm2={missing_area}')
    vertices=np.asarray(positions,dtype=np.float32); triangles=np.asarray(indices,dtype=np.int32)
    box=Bnd_Box();BRepBndLib.Add_s(shape,box)
    print(f'Tessellated {faces:,} faces, {len(triangles):,} triangles in {time.monotonic()-start:.1f}s',flush=True)
    return vertices,triangles,{'faces':faces,'triangles':len(triangles),'bounds':list(box.Get()),'linearDeflectionMm':linear,'angularDeflectionRad':angular,'degenerateFacesOmitted':missing,'omittedAreaMm2':missing_area,'totalAreaMm2':total_area,'coloredFaces':colored_faces},np.asarray(vertex_colors,dtype=np.uint8) if styles is not None else None

def main():
    p=argparse.ArgumentParser()
    p.add_argument('--source',type=Path,default=Path('RMUC2026_V2.0.0.stp'))
    p.add_argument('--cache',type=Path,default=Path.home()/'.cache/tcr-step-index')
    p.add_argument('--definition',type=int,default=343127)
    p.add_argument('--linear',type=float,default=5)
    p.add_argument('--angular',type=float,default=.5)
    args=p.parse_args()
    idx=StepIndex(args.source,args.cache)
    out=args.cache/'components';out.mkdir(exist_ok=True)
    subset=out/f'{args.definition}.step'
    report_path=out/f'{args.definition}.json'
    old=json.loads(report_path.read_text()) if report_path.exists() else {}
    if not subset.exists() or old.get('extractorVersion')!=2: extract(idx,args.definition,subset)
    document,tool,label=read(subset)
    from OCP.XCAFDoc import XCAFDoc_ShapeTool
    vertices,triangles,report,_=tessellate(XCAFDoc_ShapeTool.GetShape_s(label),args.linear,args.angular)
    np.savez_compressed(out/f'{args.definition}.npz',positions=vertices,indices=triangles)
    names,relations=idx.assembly()
    report.update({'definition':args.definition,'name':names[args.definition],'extractorVersion':2,'instances':[placement(idx,r).tolist() for r in relations.values() if r['child']==args.definition]})
    (out/f'{args.definition}.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report),flush=True)

if __name__=='__main__':main()
