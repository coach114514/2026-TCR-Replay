"""Split official base at its solid/free-shell boundaries; fixed and moving sets are exhaustive."""
import json
from pathlib import Path
import numpy as np
from .mechanism_cad import CACHE,write_mesh,vector
from .robot_mesh import write_mesh as write_local_mesh
from .base_armor_fit import lower_armor_fit

def inner_armor_mesh(path):
    """Rule fig5-16/5-18: square building armor, recessed team diffusers.

    Local Z is the hit face. Vertex alpha selects the existing armor lamp shader;
    body and printed base symbol stay non-emissive. Geometry is in metres.
    """
    vertices=[];faces=[];paint=[]
    def polygon(points,z,color):
        start=len(vertices);vertices.extend([[x,y,z] for x,y in points]);paint.extend([color]*len(points))
        for i in range(1,len(points)-1):faces.append([start,start+i,start+i+1])
    def rect(x,y,w,h,z,color):
        polygon([(x-w/2,y-h/2),(x+w/2,y-h/2),(x+w/2,y+h/2),(x-w/2,y+h/2)],z,color)
    def box(x,y,z,w,h,d,color):
        points=np.array([[px,py,pz] for pz in (-d/2,d/2) for py in (-h/2,h/2) for px in (-w/2,w/2)])+[x,y,z]
        for ids in ([4,5,7,6],[1,0,2,3],[0,4,6,2],[5,1,3,7],[0,1,5,4],[2,6,7,3]):
            offset=len(vertices);vertices.extend(points[ids].tolist());paint.extend([color]*4);faces.extend([[offset,offset+1,offset+2],[offset,offset+2,offset+3]])
    # Native inner cage has no sockets. Add an actual column mounting panel,
    # rails and two ties behind each new square module instead of suspending
    # a free plate in the opened cage. Decorative mount surfaces are not armor.
    box(0,.005,-.021,.159,.157,.008,[36,39,45,0])
    for x in (-.083,.083):box(x,.008,-.015,.018,.165,.040,[69,74,82,0])
    for y in (-.064,.084):box(0,y,-.015,.150,.018,.040,[59,63,70,0])
    outline=[(-.072,-.066),(-.065,-.073),(.065,-.073),(.072,-.066),(.072,.054),(.055,.063),(.024,.063),(.020,.078),(-.020,.078),(-.024,.063),(-.055,.063),(-.072,.054)]
    polygon(outline,-.014,[21,23,27,0]);polygon(outline,-.001,[17,19,23,0])
    for i,a in enumerate(outline):
        b=outline[(i+1)%len(outline)];offset=len(vertices)
        vertices.extend([[*a,-.014],[*b,-.014],[*b,-.001],[*a,-.001]]);paint.extend([[40,43,48,0]]*4);faces.extend([[offset,offset+1,offset+2],[offset,offset+2,offset+3]])
    # 111 x 98 mm effective plate; 58 mm diffusers lie beyond the hit area.
    rect(0,0,.111,.098,0,[9,11,14,0])
    for x in (-.065,.065):
        rect(x,-.003,.010,.062,.0001,[35,37,42,0]);rect(x,-.003,.005,.058,.0003,[160,160,160,255])
    for x in (-.063,.063):
        for y in (-.063,.054):
            angles=np.linspace(0,2*np.pi,12,endpoint=False);polygon([(x+np.cos(a)*.0024,y+np.sin(a)*.0024) for a in angles],.0004,[95,98,103,0])
    # Flat stylised building emblem follows the rule's building-armor mark,
    # rather than painting an infantry number on a base.
    for points in [
        [(-.022,-.023),(-.008,-.023),(.010,.011),(.003,.023)],
        [(-.006,-.023),(.022,-.023),(.006,.010),(-.001,-.004),(.006,-.017)],
        [(-.006,.007),(.008,.007),(.003,.018)],
    ]:polygon(points,.0005,[231,233,237,0])
    return write_local_mesh(path,np.asarray(vertices),np.asarray(faces),np.asarray(paint))

def main():
    dest=Path(__file__).resolve().parents[1]/'unity/Assets/StreamingAssets/replay'
    source=np.load(CACHE/'5891081-grouped-v2.npz')
    vertices,triangles,colors,groups=(source[k] for k in ('positions','indices','colors','groups'))
    solids=json.loads((CACHE/'5891081-solids.json').read_text())['solids']
    shells=json.loads((CACHE/'5891081-grouped-shells-v2.json').read_text())
    assert len(solids)==66 and len(shells)==112 and not np.any(groups==-1)
    inner='assets/mechanisms/base/inner-armor.tcrmesh'
    parts=[];moving=[];stats=[inner_armor_mesh(dest/inner)]
    placement=lower_armor_fit(vertices,triangles,groups,dest/inner)
    # The 3 outer V-shaped covers are distinct source solids. Detector housings
    # stay on the fixed core; fig4-12 preserves cover lean and raises their
    # lower edge from ~260 to312mm. No linkage constraints were supplied.
    for number,(ids,shellids) in enumerate((([1,2],[3,4,5]),([3,4],[6,7,8]),([5,6],[0,1,2]))):
        owner=ids;moving+=owner
        c=np.asarray(solids[ids[0]]['center']);radial=np.array([c[0],0,c[2]]);radial/=np.linalg.norm(radial)
        bounds=np.asarray(solids[ids[0]]['bounds']);pivot=radial*440;pivot[1]=bounds[1]+8
        axis=np.array([0.,0.,1.])
        translation=radial*180;translation[1]=52
        path=f'assets/mechanisms/base/leaf-{number}.tcrmesh';stats.append(write_mesh(dest/path,vertices,triangles[np.isin(groups,owner)],colors))
        # The exposed inner pillar has no armor sockets in this CAD. Construct
        # three fixed mounting faces inside the protection shell, aligned with
        # the three upper armor azimuths (+Z, +/-120deg), not the three covers'
        # opposite azimuths. Fitting a rear cover-side plane leaves the target
        # occluded even after opening. Figures4-12/5-16 show new inner modules.
        angle=np.deg2rad(number*120);radial=np.array([np.sin(angle),0.,np.cos(angle)])
        normal=radial*np.cos(np.deg2rad(15))+np.array([0,1,0])*np.sin(np.deg2rad(15))
        # Place the module just above the broad lower plinth: its housing
        # lower edge is~280mm, matching the native plinth top. User requested
        # a low installation rather than the previously estimated565mm.
        # Slide horizontally toward the original CLOSED protection shell. The
        # complete housing stays parallel to/inside the broad plinth slope;
        # the padded source-shell sweep supplies the stricter outer bound.
        armorcenter=radial*placement['radiusMm']+np.array([0.,placement['heightMm'],0.])
        parts.append(dict(mesh=path,pivot=vector(pivot).tolist(),axis=axis.tolist(),angle=0,translation=vector(translation).tolist(),solids=ids,shells=[],
            armor=dict(center=vector(armorcenter).tolist(),normal=normal[[0,2,1]].tolist(),half=[.0555,.049,.004])))
    fixed='assets/mechanisms/base/fixed.tcrmesh';stats.append(write_mesh(dest/fixed,vertices,triangles[~np.isin(groups,moving)],colors))
    fixedArmor=[]
    for shellid in (17,11,15):
        b=np.asarray(shells[shellid]['bounds']);center=(b[:3]+b[3:])*.5
        radial=np.array([center[0],0,center[2]]);radial/=np.linalg.norm(radial)
        normal=radial*np.cos(np.deg2rad(15))+np.array([0,1,0])*np.sin(np.deg2rad(15));center+=normal*6
        # Upper detector face normal/center are measured from source AM02 front faces.
        owner={17:26,11:20,15:23}[shellid];f=triangles[groups==owner];xyz=vertices[f]
        n=np.cross(xyz[:,1]-xyz[:,0],xyz[:,2]-xyz[:,0]);area=np.linalg.norm(n,axis=1);best=int(area.argmax());normal=n[best]/area[best]
        plane=np.dot(xyz[best].mean(axis=0),normal);units=n/np.maximum(area[:,None],1e-9)
        mask=(units@normal>.999)&(np.abs(xyz.mean(axis=1)@normal-plane)<.2)&(area>2000)
        center=(xyz[mask].mean(axis=1)*area[mask,None]).sum(axis=0)/area[mask].sum()+normal*.6
        fixedArmor.append(dict(center=vector(center).tolist(),normal=normal[[0,2,1]].tolist(),half=[.0555,.049,.004]))
    fixedArmor += [part['armor'] for part in parts]
    config=dict(schema=2,fixed=fixed,innerArmorMesh=inner,innerActiveAt=.99,innerPlacement=placement,leaves=parts,panels=fixedArmor,source='Official component 5891081, 66 solids / 112 free shells; rule figures 4-11/4-12 and5-16/5-18; user figure2 inner column',
        calibration=f'Covers spread outward180mm and rise52mm, preserving the native27.5deg lean (fig4-12 lower edge312mm versus source260mm). Intermediate linkage/180mm spread/1s timing are visualization estimates. Upper3 armor retain source positions; lower3 are new111x98mm effective square modules at350mm height and radius{placement["radiusMm"]}mm, moved horizontally outward{placement["horizontalOutwardMm"]}mm from the previous anchors. All three retain their upper-armor azimuths and75deg slope, parallel to the measured broad plinth outer planes. Complete modules and mounts fit inside those outer planes and have at least2mm clearance from the original CLOSED protective covers, verified by padded triangle collision sweep. Housing bottom~280mm meets the broad lower plinth top. Native CAD has no inner armor sockets; new mount location is a rule/user-figure-based visual estimate. Lower armor is enabled only at fully opened covers; all incomplete openings have no lower hit or aim targets.',
        sourceTriangles=len(triangles),movingSourceTriangles=int(np.isin(groups,moving).sum()),unassignedTriangles=0)
    (dest/'developer_base.json').write_text(json.dumps(config,ensure_ascii=False,indent=2))
    out=Path(__file__).resolve().parents[1]/'artifacts/developer-game';out.mkdir(parents=True,exist_ok=True)
    (out/'base-partition.json').write_text(json.dumps(dict(config=config,meshStats=stats),ensure_ascii=False,indent=2))
    print('DEVELOPER_BASE_SPLIT_OK triangles',len(triangles),'moving',config['movingSourceTriangles'],flush=True)
if __name__=='__main__':main()
