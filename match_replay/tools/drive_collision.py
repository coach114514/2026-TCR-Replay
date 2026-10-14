"""Build conservative 26-plane vehicle proxies, preserving rigid group joints.

Uses the actual developer-mode LOD0 meshes. The planes enclose every source
vertex; each closed convex proxy has fewer than 255 faces for Unity cooking.
Run with the project's CAD Python environment (numpy/scipy).
"""
from itertools import product
from pathlib import Path
import hashlib
import json
import struct
import numpy as np
from scipy.spatial import HalfspaceIntersection, ConvexHull
import vtk
from vtk.util import numpy_support as ns
from robot_mesh import write_mesh

root = Path(__file__).resolve().parents[1]
data = root / "unity/Assets/StreamingAssets/replay"
directions = np.array([v for v in product((-1, 0, 1), repeat=3) if any(v)], dtype=float)
directions /= np.linalg.norm(directions, axis=1)[:, None]
report = []
catalog = {"schema": 1, "groups": []}
def pieces(vertices, faces, split):
    if not split:
        return [("0", vertices)]
    poly=vtk.vtkPolyData();points=vtk.vtkPoints();points.SetData(ns.numpy_to_vtk(vertices,deep=True));poly.SetPoints(points)
    cells=vtk.vtkCellArray();cells.ImportLegacyFormat(ns.numpy_to_vtkIdTypeArray(np.column_stack((np.full(len(faces),3),faces)).astype(np.int64).ravel(),deep=True));poly.SetPolys(cells)
    mid=(vertices.min(0)+vertices.max(0))*.5
    result=[]
    for sides in product((0,1), repeat=3):
        clipped=poly
        for axis, side in enumerate(sides):
            origin=np.zeros(3);origin[axis]=mid[axis];normal=np.zeros(3);normal[axis]=1
            plane=vtk.vtkPlane();plane.SetOrigin(*origin);plane.SetNormal(*normal)
            cutter=vtk.vtkClipPolyData();cutter.SetInputData(clipped);cutter.SetClipFunction(plane);cutter.SetInsideOut(side==0);cutter.Update();clipped=cutter.GetOutput()
            if clipped.GetNumberOfCells()==0:break
        if clipped.GetNumberOfCells()==0:continue
        triangles=vtk.vtkTriangleFilter();triangles.SetInputData(clipped);triangles.Update();clipped=triangles.GetOutput()
        ids=ns.vtk_to_numpy(clipped.GetPolys().GetData()).reshape(-1,4)[:,1:]
        used=np.unique(ids)
        result.append(("".join(map(str,sides)),ns.vtk_to_numpy(clipped.GetPoints().GetData())[used].copy()))
    return result
for model_id in ("sweeper", "jiageng"):
    model = json.loads((data/f"assets/robots/{model_id}/model.json").read_text())
    for index, group in enumerate(model["groups"]):
        source = group["meshes"][0]
        if not group["turret"]:
            source = (f"assets/robots/sweeper-game/chassis-0.tcrmesh" if model_id == "sweeper"
                      else f"assets/robots/jiageng-game/group-{index}-0.tcrmesh")
        raw = (data/source).read_bytes()
        count = struct.unpack_from("<I", raw, 8)[0]
        vertices = np.frombuffer(raw, "<f4", count*3, 16).reshape(-1, 3).astype(float)
        index_count=struct.unpack_from("<I",raw,12)[0]
        faces=np.frombuffer(raw,"<u4",index_count,16+count*12).reshape(-1,3).astype(np.int64)
        group_meshes=[];covered=np.zeros(count,dtype=bool)
        for piece, part in pieces(vertices,faces,not group["turret"]):
            offsets = (part@directions.T).max(axis=0)+.0001
            planes = np.column_stack((directions, -offsets))
            points = HalfspaceIntersection(planes, part.mean(axis=0)).intersections
            hull = ConvexHull(points)
            triangles = hull.simplices.copy()
            for face, equation in zip(triangles, hull.equations):
                a, b, c = points[face]
                if np.dot(np.cross(b-a, c-a), equation[:3]) < 0:
                    face[1], face[2] = face[2], face[1]
            violation = float((part@hull.equations[:, :3].T+hull.equations[:, 3]).max())
            covered |= (vertices@hull.equations[:, :3].T+hull.equations[:,3]).max(axis=1)<=1e-7
            assert violation <= 1e-7 and hull.volume > 1e-12 and len(triangles) < 255
            output = data/f"assets/robots/drive-collision/{model_id}-{index}-{piece}.tcrmesh"
            stats = write_mesh(output, points, triangles);group_meshes.append(str(output.relative_to(data)))
            report.append(dict(model=model_id, group=index, piece=piece, source=source,
                           sourceSha256=hashlib.sha256(raw).hexdigest(),
                           mesh=str(output.relative_to(data)), maxSourceViolation=violation,
                           volume=float(hull.volume), **stats))
        assert covered.all(), (model_id,index,np.count_nonzero(~covered))
        catalog["groups"].append(dict(key=f"{model_id}-{index}",meshes=group_meshes))
artifact = root/"artifacts/drive-recovery"
artifact.mkdir(parents=True, exist_ok=True)
(artifact/"collision-proxies.json").write_text(json.dumps(report, indent=2))
(data/"assets/robots/drive-collision/catalog.json").write_text(json.dumps(catalog,indent=2))
print("DRIVE_COLLISION_PROXIES_OK", len(report), "closed conservative convex pieces", len(catalog["groups"]),"rigid groups")
