"""Read bounded saved SolidWorks display data locally, without a CAD vendor SDK.

Container/descriptor grammar: cadmpeg docs/formats/sldprt.md (CC BY 4.0),
https://github.com/cadmpeg/cadmpeg . No exact BRep, feature history or mate solver
is claimed. The assembly's saved XML matrices are the placement authority.
"""
from pathlib import Path
import re,struct,zlib,xml.etree.ElementTree as ET
import numpy as np

def streams(path):
    data=Path(path).read_bytes();result={};at=8;marker=b'\x14\x00\x06\x00\x08\x00'
    if data[4:8]!=b'\0\0\0\4':raise ValueError('Unsupported SolidWorks envelope')
    while True:
        at=data.find(marker,at)
        if at<0:break
        if at+26>len(data):break
        typ,crc,csz,usz,nsz=struct.unpack_from('<IIIII',data,at+6)
        end=at+26+nsz+csz
        if crc<65536 or not 0<nsz<512 or end>len(data) or usz>256_000_000:at+=6;continue
        encoded=data[at+26:at+26+nsz]
        try:
            name=bytes((b<<4&255)|(b>>4) for b in encoded).decode('utf-8')
            if not name or any(ord(c)<32 for c in name):at+=6;continue
            inflated=zlib.decompress(data[at+26+nsz:end],-15)
        except (ValueError,UnicodeError,zlib.error):at+=6;continue
        if len(inflated)!=usz or zlib.crc32(inflated)!=crc:raise ValueError('Native stream CRC/length mismatch: '+name)
        if name in result and result[name]!=inflated:raise ValueError('Conflicting stream identity')
        result[name]=inflated;at=end
    return result

def table(payload,at):
    channels=[];heads=[]
    for i,pair in enumerate([(4,8),(12,100),(12,100),(4,8),(4,8),(1,8)]):
        size,kind,flags,count=struct.unpack_from('<IIII',payload,at)
        if (size,kind)!=pair or flags!=2 or count>10_000_000:raise ValueError('Descriptor outside supported profile')
        start=at+16;end=start+size*count
        if end>len(payload):raise ValueError('Descriptor overflow')
        channels.append(np.frombuffer(payload[start:end],dtype='<f4' if i in [1,2] else '<u4' if size==4 else 'u1').copy());heads.append(count);at=end
    strips,v=channels[:2];v=v.reshape((-1,3))
    if not len(strips) or int(strips.sum())!=len(v) or not np.isfinite(v).all():raise ValueError('Invalid strips/positions')
    if heads[2] not in [0,len(v)] or not np.array_equal(channels[4],2*strips-2):raise ValueError('Channel mismatch')
    parts=[];base=0
    for length in strips:
        length=int(length)
        if length<3:raise ValueError('Empty/degenerate strip')
        t=np.column_stack((np.arange(base,base+length-2),np.arange(base+1,base+length-1),np.arange(base+2,base+length)));t[1::2]=t[1::2,[0,2,1]];parts.append(t);base+=length
    return v,np.concatenate(parts),at

def descriptor_tables(payload):
    at=0;marker=b'\x04\0\0\0\x08\0\0\0\x02\0\0\0'
    while True:
        at=payload.find(marker,at)
        if at<0:break
        try:
            v,t,end=table(payload,at);yield at,v,t;at=end
        except (ValueError,struct.error):at+=4

def cstrings(payload):
    for m in re.finditer(b'\xff\xfe\xff',payload):
        at=m.end();count=payload[at]
        if count==255:continue # This asset uses the short CString grammar.
        end=at+1+count*2
        try:text=payload[at+1:end].decode('utf-16le')
        except UnicodeError:continue
        yield m.start(),end,text

def assembly(data):
    root=ET.fromstring(data['swXmlContents/COMPINSTANCETREE'])
    models={x.attrib['id']:x for x in root.iter() if x.tag.endswith('swModel')}
    files={x.attrib['id']:x.attrib['swPath'] for x in root.iter() if x.tag.endswith('swFile')}
    result=[]
    def walk(model,matrix,path,hidden=False):
        for ref in model:
            if not ref.tag.endswith('swReference'):continue
            a=ref.attrib;n=a['swName']+'-'+a['swReferenceNumber']+'@'+model.attrib['swName'];other=models[a['swModelRef']]
            values=[float(v) for v in a['swTransform'].split()]
            if len(values)!=16 or not np.isfinite(values).all():raise ValueError('Invalid native occurrence matrix')
            transform=matrix@np.array(values).reshape((4,4)).T
            hide=hidden or a.get('swHidden')=='YES' or a.get('swSuppressed')=='YES'
            if any(c.tag.endswith('swReference') for c in other):walk(other,transform,path+[n],hide)
            else:result.append({'path':'/'.join(path+[n]),'name':a['swName'],'matrix':transform,'hidden':hide,'sourcePath':files[other.attrib['swFileRef']]})
    walk(models['2'],np.eye(4),[])
    return result

def cached_meshes(data):
    directory=data['FaceTessellations/Directory'];ss=list(cstrings(directory));bindings={}
    for i,(start,end,text) in enumerate(ss):
        if not text or '@' not in text or i+1>=len(ss):continue
        next_start,e2,chunk=ss[i+1]
        if next_start!=end+28:continue
        bindings[text]={'hash':directory[end+20:end+28].hex(),'previous':directory[end+12:end+20].hex(),'chunk':chunk,'index':struct.unpack_from('<i',directory,e2)[0]}
    meshes={};diagnostics=[]
    for name,payload in data.items():
        if not name.startswith('FaceTessellations/000-'):continue
        body=-1;remaining=0;identity=None
        for at,v,t in descriptor_tables(payload):
            if at<56:continue
            index=struct.unpack_from('<I',payload,at-56)[0]
            if index==body+1:
                candidate=payload[at-52:at-44].hex();count=struct.unpack_from('<I',payload,at-44)[0]
                if candidate!='0000000000000000' and 0<count<100000:
                    if remaining:diagnostics.append({'stream':name,'body':body,'omittedOrUnsupportedFaceRecords':remaining})
                    body=index;identity=candidate;remaining=count
            if identity is None:continue
            meshes.setdefault(identity,[]).append((v,t));remaining-=1
    packed={}
    for identity,parts in meshes.items():
        vs=[];ts=[];offset=0
        for v,t in parts:vs.append(v);ts.append(t+offset);offset+=len(v)
        packed[identity]=(np.concatenate(vs),np.concatenate(ts))
    return bindings,packed,diagnostics
