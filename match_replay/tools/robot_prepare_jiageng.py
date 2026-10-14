"""Prepare part meshes and source transforms for the two JiaGeng STEP poses."""
import argparse,json,re
from pathlib import Path
import numpy as np
from match_replay.tools.step_convert import read,tessellate
from match_replay.tools.step_colors import collect_colors
from OCP.XCAFDoc import XCAFDoc_ShapeTool
from OCP.TDF import TDF_Label,TDF_LabelSequence,TDF_Tool
from OCP.TCollection import TCollection_AsciiString
from OCP.TDataStd import TDataStd_Name
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('mode',choices=['expanded','folded'])
args=parser.parse_args()
cache=Path.home()/'.cache/tcr-robot-models';out=cache/'jiageng-parts';out.mkdir(parents=True,exist_ok=True)
def key(l):
 s=TCollection_AsciiString();TDF_Tool.Entry_s(l,s);return s.ToCString()
def name(l):
 a=TDataStd_Name();return a.Get().ToExtString() if l.FindAttribute(TDataStd_Name.GetID_s(),a) else key(l)
def mat(loc):
 t=loc.Transformation();return np.array([[t.Value(r,c) for c in range(1,5)] for r in range(1,4)]+[[0,0,0,1]])
mode=args.mode;doc,tool,root=read(cache/f'jiageng-{mode}.step');defs={};occ=[]
def walk(l,m,path,ancestors):
 referred=TDF_Label();definition=l
 if XCAFDoc_ShapeTool.IsReference_s(l):
  tool.GetReferredShape_s(l,referred);definition=referred;m=m@mat(tool.GetLocation_s(l))
 n=name(definition);k=key(definition);seq=TDF_LabelSequence();tool.GetComponents_s(definition,seq,False)
 if seq.Length():
  for i in range(1,seq.Length()+1):walk(seq.Value(i),m,path+[i],ancestors+[n])
 else:
  defs[k]=definition;occ.append({'key':k,'name':n,'path':path,'ancestors':ancestors,'matrix':m.tolist()})
walk(root,np.eye(4),[],[])
(out/f'{mode}.json').write_text(json.dumps({'root':name(root),'occurrences':occ},ensure_ascii=False,indent=2))
print('OCCURRENCES',mode,len(occ),len(defs),flush=True)
if mode=='expanded':
 excluded=re.compile(r'螺[钉母栓]|垫[圈片]|轴承|销[轴钉]|圆柱销|齿[轮圈]|弹簧|滚针|卡[簧环]|紧定|平头|盘头|杯头|沉头|内六角|GB[/T ]|ISO[_ -]|DIN|SKF|^M[23456][xX-]|^M[23456]_|^.*螺柱|铜柱|螺丝',re.I)
 report=[]
 for i,(k,l) in enumerate(defs.items()):
  n=name(l);target=out/(k.replace(':','_')+'.npz');omit=bool(excluded.search(n));item={'key':k,'name':n,'omitted':omit}
  if not omit and not target.exists():
   print('PART',i+1,len(defs),n,flush=True)
   styles=collect_colors(l);v,t,r,c=tessellate(tool.GetShape_s(l),2,.8,styles=styles)
   np.savez_compressed(target,positions=v,indices=t,colors=c);item.update(r)
  elif target.exists():
   d=np.load(target);item['triangles']=len(d['indices'])
  report.append(item)
 (out/'definitions.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
 print('PARTS_DONE',flush=True)
