"""AM02 CAD front surfaces, recessed light diffusers and flat printed numerals.

Only derived meshes are written. Web reference photos are not runtime textures.
Run with the existing tcr-cad-venv after robot_prepare_jiageng expanded.
"""
from pathlib import Path
import hashlib,json,shutil
import numpy as np
import vtk
from vtk.util import numpy_support as ns
from scipy.spatial import ConvexHull
from fontTools.ttLib import TTFont
from fontTools.pens.basePen import BasePen
from match_replay.tools.robot_mesh import write_mesh

ROOT=Path('match_replay/unity/Assets/StreamingAssets/replay')
DEST=ROOT/'assets/robots/armor';DEST.mkdir(parents=True,exist_ok=True)
CACHE=Path.home()/'.cache/tcr-robot-models/jiageng-parts'
FONT=Path('/usr/share/fonts/truetype/liberation2/LiberationSans-Bold.ttf')

class OutlinePen(BasePen):
    def __init__(self,glyphs):super().__init__(glyphs);self.contours=[];self.points=[]
    def _moveTo(self,p):self.points=[np.array(p,float)]
    def _lineTo(self,p):self.points.append(np.array(p,float))
    def _qCurveToOne(self,p1,p2):
        p0=self.points[-1];p1=np.array(p1);p2=np.array(p2)
        for s in np.linspace(0,1,22)[1:]:self.points.append((1-s)**2*p0+2*s*(1-s)*p1+s*s*p2)
    def _curveToOne(self,p1,p2,p3):
        p0=self.points[-1];p1=np.array(p1);p2=np.array(p2);p3=np.array(p3)
        for s in np.linspace(0,1,28)[1:]:self.points.append((1-s)**3*p0+3*s*(1-s)**2*p1+3*s*s*(1-s)*p2+s**3*p3)
    def _closePath(self):
        if np.linalg.norm(self.points[0]-self.points[-1])<1e-9:self.points.pop()
        self.contours.append(np.array(self.points));self.points=[]

def triangulate(contours):
    vertices=np.concatenate(contours);points=vtk.vtkPoints();points.SetData(ns.numpy_to_vtk(np.column_stack((vertices,np.zeros(len(vertices)))),deep=True))
    lines=vtk.vtkCellArray();offset=0
    for contour in contours:
        for i in range(len(contour)):
            lines.InsertNextCell(2);lines.InsertCellPoint(offset+i);lines.InsertCellPoint(offset+(i+1)%len(contour))
        offset+=len(contour)
    data=vtk.vtkPolyData();data.SetPoints(points);data.SetLines(lines)
    tool=vtk.vtkContourTriangulator();tool.SetInputData(data);tool.Update()
    if tool.GetTriangulationError():raise ValueError('Stencil outline triangulation failed')
    out=tool.GetOutput();tri=ns.vtk_to_numpy(out.GetPolys().GetConnectivityArray()).reshape((-1,3)).copy()
    if not len(tri):raise ValueError('Empty stencil')
    v=ns.vtk_to_numpy(out.GetPoints().GetData()).copy();n=np.cross(v[tri[:,1]]-v[tri[:,0]],v[tri[:,2]]-v[tri[:,0]])[:,2]
    tri[n<0]=tri[n<0][:,[0,2,1]]
    return v,tri

def local(vertices):
    # armor_anchor's basis is right=-native Z, up=native Y, forward=native X.
    # Its origin is 3 mm ahead of the native module's outer face. Return to the
    # physical CAD surfaces, plus a 0.2 mm separation to avoid z-fighting.
    return np.column_stack((-vertices[:,2],vertices[:,1],vertices[:,0]-9.5-3+.2))*.001

item=next(x for x in json.loads((CACHE/'definitions.json').read_text()) if '装甲模块AM02' in x['name'])
source=CACHE/(item['key'].replace(':','_')+'.npz');data=np.load(source)
v=data['positions'].astype(float);center=(v.min(0)+v.max(0))/2;v-=center;t=data['indices'];q=v[t]
normal=np.cross(q[:,1]-q[:,0],q[:,2]-q[:,0]);norm=np.linalg.norm(normal,axis=1);c=q.mean(1)
surface=(q[:,:,0].min(1)>=6.49)&(normal[:,0]>.05*norm)
faces=t[surface];ids,back=np.unique(faces,return_inverse=True);fv=v[ids];ft=back.reshape((-1,3));fc=c[surface]
lamp=(fc[:,0]>6.65)&(fc[:,0]<8.5)&(np.abs(fc[:,2])>62)&(fc[:,1]>-32)&(fc[:,1]<27)
screws=(np.abs(fc[:,2])>42)&(np.abs(fc[:,2])<54)&((fc[:,1]>43)|(fc[:,1]<-48))
colors=np.tile([13,15,18,0],(len(faces),1));colors[screws]=[78,80,84,0];colors[lamp]=[0,0,0,255]
# Paint seams use separate vertices, so screws and diffusers never bleed into the
# plate surface during interpolation. The physical CAD triangulation is retained.
front_v=local(v[faces].reshape((-1,3)));front_t=np.arange(len(front_v)).reshape((-1,3));front_c=np.repeat(colors,3,axis=0)
parts=[(front_v,front_t,front_c)];measure=[]
for side in (-1,1):
    glass=(np.abs(v[:,0]-6.8)<.025)&(v[:,2]*side>62)&(v[:,1]>-32)&(v[:,1]<27)
    yz=v[glass][:,[2,1]];hull=yz[ConvexHull(yz).vertices];mid=(hull.min(0)+hull.max(0))/2
    # Bright core lies inside the existing diffuser instead of filling the bezel.
    core=(hull-mid)*[.56,.94]+mid;cv,ct=triangulate([core]);native=np.column_stack((np.full(len(cv),6.8+.15),cv[:,1],cv[:,0]));cv=local(native)
    # local X=-native Z reverses polygon winding from the YZ outline.
    n=np.cross(cv[ct[:,1]]-cv[ct[:,0]],cv[ct[:,2]]-cv[ct[:,0]])[:,2];ct[n<0]=ct[n<0][:,[0,2,1]]
    parts.append((cv,ct,np.tile([160,160,160,255],(len(cv),1))))
    measure.append({'side':side,'centerNativeYZmm':mid[::-1].tolist(),'widthMm':float(np.ptp(hull[:,0])),'heightMm':float(np.ptp(hull[:,1]))})
# Thin dark vinyl backing matches the tall number labels in the real photos,
# covering the molded central seam without covering any corner fasteners.
corners=[];radius=.0016
for cx,cy,start in ((.043-radius,.056-radius,0),(-.043+radius,.056-radius,90),(-.043+radius,-.056+radius,180),(.043-radius,-.056+radius,270)):
    for angle in np.linspace(start,start+90,8):
        radians=np.deg2rad(angle);corners.append([cx+radius*np.cos(radians),cy+radius*np.sin(radians)-.0024825])
label_v,label_t=triangulate([np.array(corners)]);label_v[:,2]=-.00265
parts.append((label_v,label_t,np.tile([8,9,11,0],(len(label_v),1))))
offset=0;vs=[];ts=[];cs=[]
for points,faces,paint in parts:vs.append(points);ts.append(faces+offset);cs.append(paint);offset+=len(points)
report=write_mesh(DEST/'am02-surface.tcrmesh',np.concatenate(vs),np.concatenate(ts),np.concatenate(cs))
font=TTFont(FONT);glyphs=font.getGlyphSet();numbers={}
for digit in ('3','4'):
    pen=OutlinePen(glyphs);glyphs[font.getBestCmap()[ord(digit)]].draw(pen)
    points=np.concatenate(pen.contours);lo,hi=points.min(0),points.max(0);scale=.100/(hi[1]-lo[1]);mid=(lo+hi)/2
    contours=[(p-mid)*scale for p in pen.contours];pv,pt=triangulate(contours)
    # Outside-facing +Z surface is viewed from +Z: screen-right is local -X.
    pv[:,0]*=-1;pv[:,1]-=.0024825;pv[:,2]=-.00245;pt=pt[:,[0,2,1]]
    numbers[digit]=write_mesh(DEST/f'number-{digit}.tcrmesh',pv,pt,np.tile([235,237,232,0],(len(pv),1)))
copyright=Path('/usr/share/doc/fonts-liberation2/copyright');shutil.copy2(copyright,DEST/'number-font-license.txt')
manifest={'schema':1,'source':'原嘉庚 STEP 的 AM02 显示几何；扫地 AM02 的同型开口与外形核对','nativePartCacheSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'surface':report,'lightDiffusers':measure,'numbers':numbers,'numberFont':'Liberation Sans Bold outlines (SIL OFL 1.1), flat 100 mm stencil','labelBackingMm':[86,112],'overlaySeparationMm':.2,'references':['https://www.robomaster.com/zh-CN/products/components/detail/2600','https://bbs.robomaster.com/article/9306','https://oshwhub.com/ekkokoi/robomaster-mecha-master-hand-held-armor-plate-hit-feedback']}
(DEST/'surface.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
print('ARMOR_SURFACE_OK',report,'diffusers',measure,'numbers',numbers,flush=True)
