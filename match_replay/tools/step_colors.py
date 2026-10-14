"""Restore source STEP surface styles and export colored component meshes.

Presentation styles reference geometry in the reverse direction and therefore
are absent from a geometry-only dependency closure. Reattach relevant original
styles in a presentation representation; no source CAD is modified.
"""
from __future__ import annotations
import argparse
import json
import mmap
from pathlib import Path
import re
import numpy as np
from match_replay.tools.step_index import StepIndex, START
from match_replay.tools.step_convert import read, tessellate

def style_index(index):
    cache=index.cache/'surface_styles.json'
    stamp=json.loads((index.cache/'stamp.json').read_text())
    if cache.exists():
        old=json.loads(cache.read_text())
        if old['source']==stamp: return old
    styles=[]
    for m in re.finditer(rb'^#(\d+)=STYLED_ITEM\(([^;]+)\);',index.data,re.M):
        target=int(re.findall(rb'#(\d+)',m[2])[-1])
        kind=index.record(target).split(b'=',1)[1].split(b'(',1)[0]
        if kind in (b'ADVANCED_FACE',b'OPEN_SHELL',b'CLOSED_SHELL',b'MANIFOLD_SOLID_BREP',b'BREP_WITH_VOIDS'):
            styles.append([int(m[1]),target])
    presentation=next(re.finditer(rb'^#(\d+)=MECHANICAL_DESIGN_GEOMETRIC_PRESENTATION_REPRESENTATION\(',index.data,re.M))
    context=index.refs(int(presentation[1]))[-1]
    result={'source':stamp,'styles':styles,'context':context}
    cache.write_text(json.dumps(result,separators=(',',':')))
    print('SURFACE_STYLE_INDEX',len(styles),flush=True)
    return result

def attach(index,registry,definition):
    folder=index.cache/'components'
    source=folder/f'{definition}.step';destination=folder/f'{definition}-colored.step'
    with source.open('rb') as f,mmap.mmap(f.fileno(),0,access=mmap.ACCESS_READ) as data:
        geometry={int(m[1]) for m in START.finditer(data)}
        selected=[style for style,target in registry['styles'] if target in geometry]
        added=set();pending=selected+[registry['context']]
        while pending:
            eid=pending.pop()
            if eid in geometry or eid in added: continue
            added.add(eid);pending.extend(index.refs(eid))
        with destination.open('wb') as out:
            out.write(data[:data.rfind(b'ENDSEC;')])
            for eid in sorted(added):out.write(index.record(eid)+b'\n')
            if selected:
                refs=','.join(f'#{eid}' for eid in selected)
                out.write(f"#{len(index.offsets)+1}=MECHANICAL_DESIGN_GEOMETRIC_PRESENTATION_REPRESENTATION('',({refs}),#{registry['context']});\n".encode())
            out.write(b'ENDSEC;\nEND-ISO-10303-21;\n')
    print('ATTACHED_SURFACE_STYLES',definition,len(selected),flush=True)
    return destination,len(selected)

class FaceColors:
    def __init__(self):
        from OCP.TopTools import TopTools_IndexedMapOfShape
        self.faces=TopTools_IndexedMapOfShape();self.values=[None]
    def put(self,shape,color):
        i=self.faces.Add(shape)
        if i==len(self.values):self.values.append(color)
        else:self.values[i]=color
    def get(self,shape):return self.values[self.faces.FindIndex(shape)]
    def __len__(self):return self.faces.Extent()

def collect_colors(label):
    from OCP.XCAFPrs import XCAFPrs,XCAFPrs_IndexedDataMapOfShapeStyle
    from OCP.TopLoc import TopLoc_Location
    from OCP.TopExp import TopExp_Explorer
    from OCP.TopAbs import TopAbs_FACE,TopAbs_FORWARD
    styles=XCAFPrs_IndexedDataMapOfShapeStyle()
    XCAFPrs.CollectStyleSettings_s(label,TopLoc_Location(),styles)
    entries=[]
    for i in range(1,styles.Extent()+1):
        shape=styles.FindKey(i);style=styles.FindFromIndex(i)
        if style.IsSetColorSurf():entries.append((int(shape.ShapeType()),shape,style.GetColorSurf()))
    colors=FaceColors()
    # Parent colors first, explicit face colors last.
    for _,shape,color in sorted(entries,key=lambda item:item[0]):
        rgb=np.rint(np.clip([color.Red(),color.Green(),color.Blue()],0,1)*255).astype(np.uint8)
        explorer=TopExp_Explorer(shape,TopAbs_FACE)
        while explorer.More():
            # Python wrappers have identity equality; use OCCT's shape map to
            # compare the actual topology and location instead.
            colors.put(explorer.Current().Oriented(TopAbs_FORWARD),rgb);explorer.Next()
    print('XCAF_COLORS',styles.Extent(),len(colors),'faces',flush=True)
    return colors

def main():
    p=argparse.ArgumentParser()
    p.add_argument('--source',type=Path,default=Path('RMUC2026_V2.0.0.stp'))
    p.add_argument('--cache',type=Path,default=Path.home()/'.cache/tcr-step-index')
    p.add_argument('--definition',type=int,nargs='+',default=[343127,740421,1402169,2702539,3648370,5891081,6919696,7011105,181869,196117,41070,56906])
    args=p.parse_args()
    index=StepIndex(args.source,args.cache);registry=style_index(index)
    for definition in args.definition:
        report_path=args.cache/'components'/f'{definition}-colored.json'
        if report_path.exists() and json.loads(report_path.read_text()).get('source')==registry['source'] and json.loads(report_path.read_text()).get('colorPipelineVersion')==2:
            print('COLOR_CACHE_HIT',definition,flush=True);continue
        path,count=attach(index,registry,definition)
        document,tool,label=read(path)
        from OCP.XCAFDoc import XCAFDoc_ShapeTool
        vertices,triangles,report,colors=tessellate(XCAFDoc_ShapeTool.GetShape_s(label),styles=collect_colors(label))
        np.savez_compressed(args.cache/'components'/f'{definition}-colored.npz',positions=vertices,indices=triangles,colors=colors)
        if count and report['coloredFaces']==0: raise RuntimeError('Source styles did not bind to any surface')
        report.update({'definition':definition,'source':registry['source'],'sourceSurfaceStyles':count,'colorPipelineVersion':2})
        report_path.write_text(json.dumps(report,indent=2))
        print('COLOR_COMPONENT_OK',definition,'palette',len(np.unique(colors,axis=0)),flush=True)

if __name__=='__main__':main()
