"""GNOME/Wayland physical refresh adapter with temporary changes and watchdog.

Query is read-only. Apply is invoked only by the replay settings Apply button.
It preserves monitor layout, scale, rotation, resolution and other connectors.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import time

def proxy():
    from gi.repository import Gio
    return Gio.DBusProxy.new_for_bus_sync(Gio.BusType.SESSION,Gio.DBusProxyFlags.NONE,None,
        'org.gnome.Mutter.DisplayConfig','/org/gnome/Mutter/DisplayConfig','org.gnome.Mutter.DisplayConfig',None)

def state(p):
    from gi.repository import Gio
    return p.call_sync('GetCurrentState',None,Gio.DBusCallFlags.NONE,4000,None).unpack()

def snapshot(current):
    serial,monitors,logical,props=current
    physical={m[0][0]:m for m in monitors}
    config=[]
    for x,y,scale,rotation,primary,specs,_ in logical:
        outputs=[]
        for spec in specs:
            m=physical[spec[0]];mode=next(v for v in m[1] if v[6].get('is-current'))
            properties={}
            if 'is-underscanning' in m[2]:properties['underscanning']=bool(m[2]['is-underscanning'])
            if 'color-mode' in m[2]:properties['color-mode']=int(m[2]['color-mode'])
            outputs.append([spec[0],mode[0],properties])
        config.append([x,y,scale,rotation,primary,outputs])
    return dict(logical=config,layoutMode=props.get('layout-mode') if props.get('supports-changing-layout-mode',False) else None,confirmed=False)

def apply_config(p,saved,method=1):
    from gi.repository import Gio,GLib
    serial,*_=state(p);logical=[]
    for x,y,scale,rotation,primary,outputs in saved['logical']:
        out=[]
        for connector,mode,props in outputs:
            properties={key:GLib.Variant('b' if isinstance(value,bool) else 'u',value) for key,value in props.items()}
            out.append((connector,mode,properties))
        logical.append((x,y,scale,rotation,primary,out))
    properties={}
    if saved['layoutMode'] is not None:properties['layout-mode']=GLib.Variant('u',saved['layoutMode'])
    args=GLib.Variant('(uua(iiduba(ssa{sv}))a{sv})',(serial,method,logical,properties))
    p.call_sync('ApplyMonitorsConfig',args,Gio.DBusCallFlags.NONE,4000,None)

def query(p):
    current=state(p);serial,monitors,logical,props=current
    main=next((l for l in logical if l[4]),logical[0]);connector=main[5][0][0]
    monitor=next(m for m in monitors if m[0][0]==connector)
    mode=next(v for v in monitor[1] if v[6].get('is-current'))
    supported=[]
    for hz in (60,120,144,165,240):
        candidates=[v for v in monitor[1] if v[1:3]==mode[1:3] and abs(v[3]-hz)<1 and any(abs(s-main[2])<.001 for s in v[5])]
        if candidates:
            best=min(candidates,key=lambda v:abs(v[3]-hz));supported.append(dict(hz=hz,actual=best[3],id=best[0]))
    return dict(ok=True,backend='gnome-mutter',connector=connector,width=mode[1],height=mode[2],currentHz=mode[3],modes=supported)

def atomic_json(path,data):
    path.parent.mkdir(parents=True,exist_ok=True);temp=path.with_suffix('.tmp');temp.write_text(json.dumps(data));temp.replace(path)

def main():
    parser=argparse.ArgumentParser();parser.add_argument('action',choices=['query','apply','restore','confirm','watch','verify'])
    parser.add_argument('--hz',type=int);parser.add_argument('--snapshot',type=Path);parser.add_argument('--pid',type=int,default=0)
    args=parser.parse_args()
    if args.action=='confirm':
        saved=json.loads(args.snapshot.read_text());saved['confirmed']=True;atomic_json(args.snapshot,saved);print(json.dumps(dict(ok=True)));return
    if args.action=='watch':
        deadline=time.monotonic()+15
        while time.monotonic()<deadline:
            saved=json.loads(args.snapshot.read_text())
            if saved.get('confirmed'):return
            if args.pid:
                try:os.kill(args.pid,0)
                except ProcessLookupError:break
            time.sleep(.25)
        apply_config(proxy(),saved);return
    p=proxy()
    if args.action=='query':print(json.dumps(query(p)));return
    if args.action=='restore':
        saved=json.loads(args.snapshot.read_text());apply_config(p,saved);saved['confirmed']=True;atomic_json(args.snapshot,saved);print(json.dumps(query(p)));return
    current=query(p)
    if args.hz not in (60,120,144,165,240):raise ValueError('Unsupported refresh preset')
    selected=next((m for m in current['modes'] if m['hz']==args.hz),None)
    if selected is None:raise ValueError('Requested refresh not supported at current resolution/scale')
    saved=snapshot(state(p));desired=json.loads(json.dumps(saved))
    for logical in desired['logical']:
        for output in logical[5]:
            if output[0]==current['connector']:output[1]=selected['id']
    apply_config(p,desired,method=0) # compositor validation, no display change
    if args.action=='verify':print(json.dumps(dict(ok=True,validatedHz=args.hz)));return
    if args.snapshot is None:raise ValueError('Snapshot is required')
    atomic_json(args.snapshot,saved)
    # Start recovery before changing the display, including a failed/aborted UI.
    subprocess.Popen([sys.executable,__file__,'watch','--snapshot',str(args.snapshot),'--pid',str(args.pid)],
                     stdin=subprocess.DEVNULL,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,start_new_session=True)
    apply_config(p,desired)
    print(json.dumps(query(p)))

if __name__=='__main__':
    try:main()
    except Exception as error:
        print(json.dumps(dict(ok=False,error=str(error))));sys.exit(1)
