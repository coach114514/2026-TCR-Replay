"""Partition original JiaGeng armor casings without losing source triangles.
Run using the CAD virtual environment with NumPy and SciPy.
"""
import json,struct
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation
r=Path(__file__).resolve().parents[1]/'unity/Assets/StreamingAssets/replay';m=json.loads((r/'assets/robots/jiageng/model.json').read_text());dest=r/'assets/robots/jiageng-game';dest.mkdir(exist_ok=True)
def read(p):
 d=p.read_bytes();nv,ni=struct.unpack_from('<II',d,8);v=np.frombuffer(d,'<f4',nv*3,16).reshape(-1,3).copy();f=np.frombuffer(d,'<u4',ni,16+nv*12).reshape(-1,3).copy();c=np.frombuffer(d,'u1',nv*4,16+nv*12+ni*4).reshape(-1,4).copy();return v,f,c
def write(p,v,f,c):
 ids,b=np.unique(f.flatten(),return_inverse=True);v=v[ids];c=c[ids];f=b.reshape(-1,3)
 with p.open('wb') as o:o.write(struct.pack('<8sII',b'TCRMESH2',len(v),f.size));o.write(v.astype('<f4').tobytes());o.write(f.astype('<u4').tobytes());o.write(c.astype('u1').tobytes())
report=[]
for lod in range(3):
 pieces=[[] for _ in m['armorPanels']];total=fixed=0
 for g,group in enumerate(m['groups']):
  if group['turret']:continue
  v,f,c=read(r/group['meshes'][lod]);owner=np.full(len(f),-1);centers=v[f].mean(1);total+=len(f)
  for i,panel in enumerate(m['armorPanels']):
   pos=np.array(panel['position']);rot=Rotation.from_quat(panel['rotation']).as_matrix();local=(centers-pos)@rot
   mask=(abs(local[:,0])<.084)&(abs(local[:,1])<.078)&(local[:,2]>-.053)&(local[:,2]<.024)&(owner<0);owner[mask]=i
   fv=f[mask]
   if len(fv):pieces[i].append(((v-pos)@rot,fv,c))
  write(dest/f'group-{g}-{lod}.tcrmesh',v,f[owner<0],c);fixed+=int(sum(owner<0))
 sizes=[]
 for i,parts in enumerate(pieces):
  if not parts: sizes.append(0);continue
  vv=[];ff=[];cc=[];off=0
  for v,f,c in parts:vv.append(v);ff.append(f+off);cc.append(c);off+=len(v)
  vf=np.concatenate(ff);write(dest/f'armor-{i}-{lod}.tcrmesh',np.concatenate(vv),vf,np.concatenate(cc));sizes.append(len(vf))
 assert fixed+sum(sizes)==total
 report.append(dict(lod=lod,source=total,fixed=fixed,armor=sizes))
out=Path(__file__).resolve().parents[1]/'artifacts/phase2';out.mkdir(exist_ok=True);(out/'jiageng-armor-partition.json').write_text(json.dumps(report,indent=2));print(report)
