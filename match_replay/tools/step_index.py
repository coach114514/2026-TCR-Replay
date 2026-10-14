"""Read-only STEP index and component extraction for large official assemblies.

Stores byte offsets instead of loading millions of CAD entities into Python.
Geometry dependencies are closed before handing a component to OpenCascade.
"""
from __future__ import annotations
import argparse
from array import array
import json
import mmap
from pathlib import Path
import re

START = re.compile(rb'^#(\d+)=', re.M)
REF = re.compile(rb'#(\d+)')
META = (b'PRODUCT(', b'PRODUCT_DEFINITION(', b'PRODUCT_DEFINITION_FORMATION',
        b'PRODUCT_DEFINITION_SHAPE(', b'SHAPE_DEFINITION_REPRESENTATION(',
        b'NEXT_ASSEMBLY_USAGE_OCCURRENCE(', b'CONTEXT_DEPENDENT_SHAPE_REPRESENTATION(',
        b'ITEM_DEFINED_TRANSFORMATION(', b'(REPRESENTATION_RELATIONSHIP(',
        b'SHAPE_REPRESENTATION_RELATIONSHIP(')

def build(source: Path, cache: Path):
    cache.mkdir(parents=True, exist_ok=True)
    stat = source.stat()
    stamp = {'source': str(source.resolve()), 'size': stat.st_size, 'mtimeNs': stat.st_mtime_ns, 'indexVersion':2}
    if (cache/'stamp.json').exists() and json.loads((cache/'stamp.json').read_text()) == stamp:
        return
    offsets = array('Q', [0]) * 8_000_000
    lengths = array('I', [0]) * 8_000_000
    metadata = {}
    previous_id = previous_start = maximum = count = 0
    with source.open('rb') as f, mmap.mmap(f.fileno(), 0, access=mmap.ACCESS_READ) as data:
        for match in START.finditer(data):
            eid, start = int(match[1]), match.start()
            if eid >= len(offsets):
                extend = eid + 1 - len(offsets)
                offsets.extend(array('Q', [0])*extend); lengths.extend(array('I', [0])*extend)
            if previous_id:
                lengths[previous_id] = start - previous_start
            offsets[eid] = start
            prefix = data[match.end():match.end()+80]
            if prefix.startswith(META):
                end = data.find(b';', match.end())+1
                metadata[str(eid)] = data[start:end].decode('ascii', errors='replace')
            previous_id, previous_start = eid, start
            maximum = max(maximum, eid); count += 1
        lengths[previous_id] = data.find(b';', previous_start)+1-previous_start
    del offsets[maximum+1:]; del lengths[maximum+1:]
    with (cache/'offsets.bin').open('wb') as f: offsets.tofile(f)
    with (cache/'lengths.bin').open('wb') as f: lengths.tofile(f)
    (cache/'metadata.json').write_text(json.dumps(metadata))
    (cache/'stamp.json').write_text(json.dumps(stamp))
    print(f'Indexed {count:,} entities, {len(metadata):,} assembly records', flush=True)

class StepIndex:
    def __init__(self, source, cache):
        self.source, self.cache = Path(source), Path(cache)
        build(self.source, self.cache)
        self.handle = self.source.open('rb')
        self.data = mmap.mmap(self.handle.fileno(), 0, access=mmap.ACCESS_READ)
        self.offsets = array('Q'); self.lengths = array('I')
        with (self.cache/'offsets.bin').open('rb') as f: self.offsets.fromfile(f,(self.cache/'offsets.bin').stat().st_size//8)
        with (self.cache/'lengths.bin').open('rb') as f: self.lengths.fromfile(f,(self.cache/'lengths.bin').stat().st_size//4)
        self.meta = {int(k):v for k,v in json.loads((self.cache/'metadata.json').read_text()).items()}

    def record(self, eid):
        if eid >= len(self.offsets) or not self.offsets[eid]:
            raise ValueError(f'Missing STEP dependency #{eid}')
        return self.data[self.offsets[eid]:self.offsets[eid]+self.lengths[eid]].strip()

    def refs(self, eid):
        body=re.sub(rb"'(?:''|[^'])*'",b"''",self.record(eid).split(b'=',1)[1])
        return [int(m[1]) for m in REF.finditer(body)]

    def products(self):
        products = {eid: re.findall(r"'([^']*)'", r)[0] for eid,r in self.meta.items() if '=PRODUCT(' in r}
        formations = {eid:self.refs(eid)[0] for eid,r in self.meta.items() if '=PRODUCT_DEFINITION_FORMATION' in r}
        definitions = {eid:formations[self.refs(eid)[0]] for eid,r in self.meta.items() if '=PRODUCT_DEFINITION(' in r}
        return {eid:products[product] for eid,product in definitions.items()}

    def assembly(self):
        names = self.products()
        relationships = {}
        for eid,r in self.meta.items():
            if '=NEXT_ASSEMBLY_USAGE_OCCURRENCE(' in r:
                parent,child = self.refs(eid)
                relationships[eid] = {'parent':parent,'child':child,'name':names[child]}
        pds_to_nauo = {eid:self.refs(eid)[0] for eid,r in self.meta.items() if '=PRODUCT_DEFINITION_SHAPE(' in r and self.refs(eid)[0] in relationships}
        for eid,r in self.meta.items():
            if '=CONTEXT_DEPENDENT_SHAPE_REPRESENTATION(' not in r: continue
            relation,pds = self.refs(eid)
            nauo = pds_to_nauo[pds]
            child_rep,parent_rep,transform = self.refs(relation)
            relationships[nauo].update({'relation':relation,'context':eid,'pds':pds,'transform':transform,'childRep':child_rep,'parentRep':parent_rep})
        return names, relationships

    def extract(self, roots, destination):
        seen = set(); queue = list(roots)
        while queue:
            eid = queue.pop()
            if eid in seen: continue
            seen.add(eid); queue.extend(self.refs(eid))
        with Path(destination).open('wb') as f:
            f.write(self.data[:self.data.find(b'DATA;')+5]+b'\n')
            for eid in sorted(seen): f.write(self.record(eid)+b'\n')
            f.write(b'ENDSEC;\nEND-ISO-10303-21;\n')
        return len(seen)

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('source',type=Path)
    parser.add_argument('--cache',type=Path,default=Path.home()/'.cache/tcr-step-index')
    args=parser.parse_args()
    index=StepIndex(args.source,args.cache)
    names,relationships=index.assembly()
    children={r['child'] for r in relationships.values()}
    roots=[eid for eid in names if eid not in children]
    report={'products':names,'relationships':relationships,'roots':roots}
    (args.cache/'assembly.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
    print('Root assemblies:',[(eid,names[eid]) for eid in roots],flush=True)
    for root in roots:
        print('Direct children:',[(r['child'],r['name']) for r in relationships.values() if r['parent']==root],flush=True)

if __name__=='__main__': main()
