"""Shared, bounded offline robot mesh preparation. CAD source files stay read-only."""
from pathlib import Path
import hashlib,struct
import numpy as np

def armor_anchor(vertices,matrix,scene):
    """AM02 front-facing overlay from its actual part bounds and placement.

    Both sources use the official part's local X thickness, Y height and Z width.
    The offset is outside its physical front face; it does not change the CAD.
    """
    from scipy.spatial.transform import Rotation
    lo,hi=vertices.min(0),vertices.max(0);transform=scene@matrix
    center=transform[:3,:3]@((lo+hi)/2)+transform[:3,3]
    forward=transform[:3,0];scale=np.linalg.norm(forward);forward=forward/scale
    if np.dot(forward,center)<0:forward=-forward
    up=transform[:3,1];up=up/np.linalg.norm(up)
    right=np.cross(up,forward);right=right/np.linalg.norm(right)
    up=np.cross(forward,right)
    rotation=Rotation.from_matrix(np.column_stack((right,up,forward))).as_quat()
    position=center+forward*((hi[0]-lo[0])*scale/2+.003)
    return {'position':position.tolist(),'rotation':rotation.tolist()}

def write_mesh(path,positions,indices,colors=None):
    path=Path(path);path.parent.mkdir(parents=True,exist_ok=True)
    v=np.asarray(positions,dtype='<f4');t=np.asarray(indices,dtype='<u4')
    if not len(v) or not len(t) or not np.isfinite(v).all() or t.max()>=len(v):raise ValueError('Invalid robot geometry')
    rgba=None if colors is None else np.asarray(colors,dtype='u1')
    if rgba is not None:
        if rgba.ndim!=2 or len(rgba)!=len(v) or rgba.shape[1] not in (3,4):raise ValueError('Invalid vertex colors')
        if rgba.shape[1]==3:rgba=np.column_stack((rgba,np.full(len(v),255))).astype('u1')
    with path.open('wb') as f:
        f.write(struct.pack('<8sII',b'TCRMESH2' if rgba is not None else b'TCRMESH1',len(v),t.size));f.write(v.tobytes());f.write(t.tobytes())
        if rgba is not None:f.write(rgba.tobytes())
    return {'vertices':len(v),'triangles':len(t),'bytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()}

def decimate(v,t,target):
    """QEM per rigid motion/paint group, never across joints or paint seams."""
    import vtk
    from vtk.util import numpy_support as ns
    points,inverse=np.unique(np.round(v,5),axis=0,return_inverse=True);faces=inverse[t]
    good=(faces[:,0]!=faces[:,1])&(faces[:,1]!=faces[:,2])&(faces[:,0]!=faces[:,2]);faces=faces[good]
    if len(faces)<=target:return points,faces
    mesh=vtk.vtkPolyData();p=vtk.vtkPoints();p.SetData(ns.numpy_to_vtk(points,deep=True));mesh.SetPoints(p)
    cells=vtk.vtkCellArray();cells.ImportLegacyFormat(ns.numpy_to_vtkIdTypeArray(np.column_stack((np.full(len(faces),3),faces)).astype(np.int64).ravel(),deep=True));mesh.SetPolys(cells)
    dec=vtk.vtkQuadricDecimation();dec.SetInputData(mesh);dec.SetTargetReduction(max(0,1-target/len(faces)));dec.VolumePreservationOn();dec.Update();out=dec.GetOutput()
    verts=ns.vtk_to_numpy(out.GetPoints().GetData()).copy();tri=ns.vtk_to_numpy(out.GetPolys().GetData()).reshape((-1,4))[:,1:].copy()
    if not len(tri) or not np.isfinite(verts).all():raise ValueError('Decimation produced empty/non-finite mesh')
    return verts,tri

def simplify_painted(v,t,c,target):
    palette,groups=np.unique(c[t[:,0]],axis=0,return_inverse=True)
    if not np.all(c[t]==c[t[:,0]][:,None,:]):raise ValueError('Paint changes inside triangle')
    vv=[];tt=[];cc=[];offset=0
    for i,color in enumerate(palette):
        f=t[groups==i];ids,back=np.unique(f,return_inverse=True);points,faces=decimate(v[ids],back.reshape((-1,3)),max(8,round(target*len(f)/len(t))))
        vv.append(points);tt.append(faces+offset);cc.append(np.tile(color,(len(points),1)));offset+=len(points)
    return np.concatenate(vv),np.concatenate(tt),np.concatenate(cc)

def rigid_blend(matrix,amount):
    """Rotate about the inferred fixed axis, retaining axial translation.

    Endpoint equality is checked by callers. Unlike linear translation + slerp
    about the world origin, this keeps a hinge arc instead of cutting its chord.
    """
    from scipy.spatial.transform import Rotation
    r=matrix[:3,:3];translation=matrix[:3,3];pivot=np.linalg.lstsq(np.eye(3)-r,translation,rcond=1e-8)[0]
    residual=translation-(np.eye(3)-r)@pivot
    blend=Rotation.from_rotvec(Rotation.from_matrix(r).as_rotvec()*amount).as_matrix()
    result=np.eye(4);result[:3,:3]=blend;result[:3,3]=(np.eye(3)-blend)@pivot+residual*amount
    return result
