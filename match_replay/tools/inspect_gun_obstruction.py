"""Trace the sweeper's first-person obstruction to saved source CAD geometry."""
import json
from pathlib import Path
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.collections import PolyCollection
from matplotlib.font_manager import FontProperties
from .solidworks_display import streams, assembly, cached_meshes


def main():
    root = Path(__file__).resolve().parents[1]
    out = root/'artifacts/base-visual'
    out.mkdir(parents=True, exist_ok=True)
    source = Path.home()/'.cache/tcr-robot-models/sweeper-source/扫地机器人v2/总的1.0.SLDASM'
    data = streams(source)
    occurrences = assembly(data)
    bindings, meshes, _ = cached_meshes(data)
    model = json.loads((root/'unity/Assets/StreamingAssets/replay/assets/robots/sweeper/model.json').read_text())
    axis = np.array([[1,0,0],[0,0,-1],[0,1,0]], float)
    shift = np.asarray(model['groups'][1]['pivot'])-axis@np.array([-.0971,-.0867,.9783])
    occurrence = next(x for x in occurrences if x['name']=='中国石油大学枪管-final(1)' and not x['hidden'])
    binding = bindings[occurrence['path']]
    vertices, faces = meshes.get(binding['hash']) or meshes[binding['previous']]
    vertices = (vertices@occurrence['matrix'][:3,:3].T+occurrence['matrix'][:3,3])@axis.T+shift
    triangles = vertices[faces]
    centers = triangles.mean(1)
    edges1, edges2 = triangles[:,1]-triangles[:,0], triangles[:,2]-triangles[:,0]
    normals = np.cross(edges1, edges2)
    normals /= np.maximum(np.linalg.norm(normals,axis=1)[:,None],1e-12)
    bar = (centers[:,0]>.151)&(centers[:,1]>.290)&(centers[:,1]<.299)
    eye = np.array([.1248139657,.2895201472,.0920671801])
    forward = np.asarray(model['gunForward'])
    right = np.cross([0,1,0],forward);right /= np.linalg.norm(right)
    up = np.cross(forward,right)
    hits = []
    for pitch in (0,5,10):
        ray = forward*np.cos(np.deg2rad(pitch))+up*np.sin(np.deg2rad(pitch))
        p = np.cross(ray,edges2);det = np.sum(edges1*p,axis=1)
        inv = 1/np.where(abs(det)>1e-10,det,1e-10)
        delta = eye-triangles[:,0];u = np.sum(delta*p,axis=1)*inv
        q = np.cross(delta,edges1);v = q@ray*inv;t = np.sum(edges2*q,axis=1)*inv
        ids = np.where((abs(det)>1e-10)&(u>=0)&(v>=0)&(u+v<=1)&(t>.012))[0]
        if len(ids):
            i=ids[np.argmin(t[ids])];hits.append({'pitchDegrees':pitch,'distanceM':float(t[i]),'point':(eye+ray*t[i]).tolist()})
        else:
            hits.append({'pitchDegrees':pitch,'clear':True})
    for name, mask in [('original-gun',np.ones(len(faces),bool)),('obstructing-upper-lip',bar)]:
        selected = faces[mask]
        ids, back = np.unique(selected,return_inverse=True)
        with (out/(name+'.obj')).open('w') as file:
            file.write('# Exact saved source geometry; metres; Unity XYZ\n')
            for x,y,z in vertices[ids]:file.write(f'v {x:.9f} {y:.9f} {z:.9f}\n')
            for a,b,c in back.reshape(-1,3)+1:file.write(f'f {a} {b} {c}\n')
    font = FontProperties(fname='/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc')
    fig, axes = plt.subplots(1,3,figsize=(17,6),facecolor='#111820')
    for ax, projection, depth, title in [(axes[0],[0,1],2,'原枪管组件侧面：红色为遮挡上沿'),(axes[1],[0,2],1,'原组件俯视：C镜头在上沿后方')]:
        order = np.argsort(centers[:,depth]);paint = np.tile([.56,.60,.65],(len(faces),1));paint[bar]=[1,.20,.08]
        ax.add_collection(PolyCollection(triangles[order][:,:,projection]*1000,facecolors=paint[order],edgecolors='none',rasterized=True))
        ax.scatter(*eye[projection]*1000,color='#00e2ff',s=40,zorder=3)
        ax.annotate('C：VT03挂点',eye[projection]*1000,xytext=(50,eye[projection][1]*1000+20),fontproperties=font,color='#00e2ff',arrowprops={'arrowstyle':'->','color':'#00e2ff'})
        point = eye+forward*.09;ax.plot([eye[projection][0]*1000,point[projection][0]*1000],[eye[projection][1]*1000,point[projection][1]*1000],color='#00e2ff',linewidth=1)
        ax.autoscale();ax.set_aspect('equal');ax.set_title(title,fontproperties=font,color='white')
        ax.set_xlabel('X（沿枪管方向）/ mm',fontproperties=font,color='white');ax.set_ylabel(('Y（高度）' if projection==[0,1] else 'Z（横向）')+' / mm',fontproperties=font,color='white')
    for hit in hits:
        if 'point' in hit:
            p=np.asarray(hit['point'])*1000;axes[0].plot([eye[0]*1000,p[0]],[eye[1]*1000,p[1]],color='yellow',linewidth=1)
    relative=triangles-eye;camera=np.stack((relative@right,relative@up,relative@forward),axis=2)
    keep=np.all(camera[:,:,2]>.012,axis=1)
    projected=camera[keep,:,:2]/camera[keep,:,2,None]/np.tan(np.deg2rad(75/2));projected[:,:,0]/=1600/900
    order=np.argsort(camera[keep,:,2].mean(1))[::-1]
    paint=np.tile([.56,.60,.65],(np.count_nonzero(keep),1));paint[bar[keep]]=[1,.20,.08]
    axes[2].add_collection(PolyCollection(projected[order],facecolors=paint[order],edgecolors='none',rasterized=True))
    axes[2].set(xlim=(-1,1),ylim=(-1,1));axes[2].set_aspect(900/1600);axes[2].axhline(0,color='white',alpha=.3);axes[2].axvline(0,color='white',alpha=.3)
    axes[2].set_title('C挂点75°几何视场：上沿放大成横杠',fontproperties=font,color='white');axes[2].set_xticks([]);axes[2].set_yticks([])
    for ax in axes:
        ax.set_facecolor('#151e28');ax.tick_params(colors='white');[spine.set_color('#607080') for spine in ax.spines.values()]
    fig.suptitle('实际原建模的枪管组件上沿；中心视线无遮挡，向上5°–10°视线在约33mm处碰到上沿',fontproperties=font,color='white',fontsize=15)
    fig.tight_layout();fig.savefig(out/'sweeper-front-bar-explained.png',dpi=130)
    report={'source':str(source),'component':occurrence['name'],'path':occurrence['path'],'eyeC':eye.tolist(),'forward':forward.tolist(),
            'sourceTriangles':len(faces),'upperLipTriangles':int(bar.sum()),'rayChecks':hits,'interpretation':'原枪管组件上沿进入C相机视锥；不是场地结构，不是残留辅助面；中心视线可通，附近实体造成上方遮挡。本次保持C挂点及可见车体，不擅自移相机或删除零件。'}
    (out/'sweeper-front-bar-audit.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
    print(json.dumps(report,ensure_ascii=False,indent=2))


if __name__=='__main__':main()
