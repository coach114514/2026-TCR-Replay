"""Export a developer-only contact proxy, retaining road/riser/roof geometry.
Remove separate raised engineering components within 35 mm of the already
cleaned walkable floor. Display/replay meshes are unchanged. Run as a module
with the CAD virtual environment from the workspace root.
"""
from pathlib import Path
import json,struct
import numpy as np,vtk
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components
from vtk.util.numpy_support import numpy_to_vtk
from match_replay.tools.robot_mesh import write_mesh
root=Path('match_replay/unity/Assets/StreamingAssets/replay')
def load(p):
 b=p.read_bytes();n,i=struct.unpack_from('<ii',b,8);return np.frombuffer(b,dtype='<f4',count=n*3,offset=16).reshape(-1,3).copy(),np.frombuffer(b,dtype='<i4',count=i,offset=16+n*12).reshape(-1,3).copy()
v,t=load(root/'assets/official_final/343127.tcrmesh');gv,gt=load(root/'assets/collision/final_floor.tcrmesh')
pts=vtk.vtkPoints();pts.SetData(numpy_to_vtk(gv));cells=vtk.vtkCellArray();cells.ImportLegacyFormat(numpy_to_vtk(np.column_stack((np.full(len(gt),3),gt)).astype('i8').ravel(),array_type=vtk.VTK_ID_TYPE));pd=vtk.vtkPolyData();pd.SetPoints(pts);pd.SetPolys(cells);locator=vtk.vtkStaticCellLocator();locator.SetDataSet(pd);locator.BuildLocator()
_,inverse=np.unique(np.round(v/1e-5).astype('i8'),axis=0,return_inverse=True);welded=inverse[t];edges=np.concatenate((welded[:,[0,1]],welded[:,[1,2]],welded[:,[2,0]]));n=int(inverse.max()+1);graph=coo_matrix((np.ones(len(edges)),(edges[:,0],edges[:,1])),shape=(n,n));components,labels=connected_components(graph,directed=False);faceids=labels[welded[:,0]];world=v+np.array([14,1.941343676495,5.875656323505]);removed=[];keep=np.ones(len(t),bool)
for c in range(components):
 ids=np.unique(t[faceids==c]);p=world[ids];extent=np.ptp(p,axis=0)
 if max(extent)>.5 or extent[1]>.15:continue
 heights=[]
 for point in p[np.linspace(0,len(p)-1,min(100,len(p)),dtype=int)]:
  xyz=[0.,0.,0.];param=vtk.mutable(0.);sub=vtk.mutable(0);cell=vtk.mutable(0);pcoords=[0.,0.,0.]
  hit=locator.IntersectWithLine((point[0],point[1]+.05,point[2]),(point[0],point[1]-.1,point[2]),1e-6,param,xyz,pcoords,sub,cell)
  if not hit:break
  heights.append(point[1]-xyz[1])
 if len(heights)==min(100,len(p)) and min(heights)>-.015 and .005<max(heights)<.035:
  keep[faceids==c]=False;removed.append(dict(component=c,triangles=int((faceids==c).sum()),min=p.min(0).tolist(),max=p.max(0).tolist(),floorDistance=[min(heights),max(heights)]))
faces=t[keep];ids,back=np.unique(faces,return_inverse=True);target=root/'assets/developer/field/final_road_contact.tcrmesh';target.parent.mkdir(parents=True,exist_ok=True);report=write_mesh(target,v[ids],back.reshape(-1,3));artifact=Path('match_replay/artifacts/vehicle-physics/mechanics/field-contact.json');artifact.write_text(json.dumps(dict(source='assets/official_final/343127.tcrmesh',output=str(target.relative_to(root)),sourceTriangles=len(t),removedTriangles=int((~keep).sum()),removedComponents=removed,mesh=report,thresholds=dict(maxSpan=.5,minFloorDistance=-.015,maxFloorDistance=.035,minRaisedElevation=.005)),indent=2));print('removed',len(removed),'components',int((~keep).sum()),'triangles',report)
