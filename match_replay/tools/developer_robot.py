"""Partition the sweeper's source armor casing from chassis for editable heights."""
from pathlib import Path
import json,struct
import numpy as np
from scipy.spatial.transform import Rotation
root=Path(__file__).resolve().parents[1]/'unity/Assets/StreamingAssets/replay'
model=json.loads((root/'assets/robots/sweeper/model.json').read_text())
def write(path,v,f,c):
    ids,back=np.unique(f.flatten(),return_inverse=True);v=v[ids];c=c[ids];f=back.reshape(-1,3)
    path.parent.mkdir(parents=True,exist_ok=True)
    with path.open('wb') as out:
        out.write(struct.pack('<8sII',b'TCRMESH2',len(v),f.size));out.write(v.astype('<f4').tobytes());out.write(f.astype('<u4').tobytes());out.write(c.astype('u1').tobytes())
    return len(f)
report=[]
for lod,filename in enumerate(model['groups'][0]['meshes']):
    data=(root/filename).read_bytes();nv,ni=struct.unpack_from('<II',data,8)
    v=np.frombuffer(data,'<f4',nv*3,16).reshape(-1,3).copy();f=np.frombuffer(data,'<u4',ni,16+nv*12).reshape(-1,3).copy();c=np.frombuffer(data,'u1',nv*4,16+nv*12+ni*4).reshape(-1,4).copy()
    centers=v[f].mean(1);owner=np.full(len(f),-1,dtype=int);transforms=[]
    for i,panel in enumerate(model['armorPanels']):
        p=np.array(panel['position']);r=Rotation.from_quat(panel['rotation']).as_matrix();local=(centers-p)@r
        keep=(abs(local[:,0])<.084)&(abs(local[:,1])<.078)&(local[:,2]>-.053)&(local[:,2]<.024)&(owner<0)
        owner[keep]=i;transforms.append((p,r))
    fixed=write(root/f'assets/robots/sweeper-game/chassis-{lod}.tcrmesh',v,f[owner<0],c)
    parts=[]
    for i,(p,r) in enumerate(transforms):parts.append(write(root/f'assets/robots/sweeper-game/armor-{i}-{lod}.tcrmesh',(v-p)@r,f[owner==i],c))
    assert fixed+sum(parts)==len(f)
    report.append(dict(lod=lod,sourceTriangles=len(f),fixedTriangles=fixed,armorTriangles=parts,unassigned=0))
out=Path(__file__).resolve().parents[1]/'artifacts/developer-game';out.mkdir(parents=True,exist_ok=True)
(out/'sweeper-armor-partition.json').write_text(json.dumps(report,indent=2))
print('DEVELOPER_SWEEPER_ARMOR_SPLIT_OK',report)
