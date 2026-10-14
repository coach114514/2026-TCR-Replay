"""Validate and locally install an existing converted replay resource pack.

No downloads, recording, uploading or change to upstream asset licenses.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import tempfile
import time
from .cli import validate

ROOT=Path(__file__).resolve().parents[1]


def safe_file(source, name):
    if not isinstance(name,str) or not name or '\\' in name:
        raise ValueError(f'Invalid resource path: {name!r}')
    p=(source/name).resolve()
    if not p.is_relative_to(source) or Path(name).is_absolute() or not p.is_file():
        raise ValueError(f'Missing or escaping resource path: {name!r}')
    return p


def inspect(source):
    import gzip
    source=source.resolve()
    if not source.is_dir():raise ValueError('Source must be a converted replay folder')
    for p in source.rglob('*'):
        if p.is_symlink():raise ValueError('Resource pack must not contain symbolic links')
    catalog=json.loads(safe_file(source,'catalog.json').read_text())
    fields=json.loads(safe_file(source,'fields_catalog.json').read_text())
    if catalog.get('schema')!=1 or not catalog.get('matches') or fields.get('schema')!=1 or not fields.get('fields'):
        raise ValueError('Invalid or empty resource catalog')
    field_ids=set()
    for entry in fields['fields']:
        config=json.loads(safe_file(source,entry['file']).read_text())
        if config['id']!=entry['id'] or config.get('width',0)<=0 or config.get('depth',0)<=0 or config['id'] in field_ids:
            raise ValueError('Invalid field identity or dimensions')
        field_ids.add(config['id'])
        for name in ('mesh','texture','surfaceTexture','collisionMesh'):
            if config.get(name):safe_file(source,config[name])
        if config.get('meshFormat')=='assembly_binary_v1':
            assembly=json.loads(safe_file(source,config['mesh']).read_text())
            for part in assembly['parts']:safe_file(source,part['mesh'])
    if fields['defaultField'] not in field_ids:raise ValueError('Default field missing')
    ids=set()
    for entry in catalog['matches']:
        p=safe_file(source,entry['file']);data=json.loads(gzip.decompress(p.read_bytes()) if p.suffix=='.gz' else p.read_bytes())
        validate(data)
        if data['gameId']!=entry['gameId'] or data['gameId'] in ids or data['fieldId'] not in field_ids:
            raise ValueError('Duplicate/mismatched match identity or source field')
        ids.add(data['gameId'])
    return dict(matches=len(ids),fields=len(field_ids),files=sum(p.is_file() and p.suffix!='.meta' for p in source.rglob('*')))


def install(source,output,replace_demo=False):
    source,output=source.resolve(),output.resolve()
    if source==output or output.is_relative_to(source) or source.is_relative_to(output):
        raise ValueError('Source and destination must be separate folders')
    result=inspect(source)
    occupied=output.exists() and any(output.iterdir())
    if occupied:
        profile=output/'resource_profile.json'
        if not replace_demo or not profile.exists() or json.loads(profile.read_text()).get('kind')!='synthetic-demo':
            raise ValueError('Only an existing synthetic demo can be replaced using --replace-demo; choose a fresh --output otherwise')
    output.parent.mkdir(parents=True,exist_ok=True)
    staged=Path(tempfile.mkdtemp(prefix='.replay-import-',dir=output.parent));backup=None
    try:
        shutil.copytree(source,staged,dirs_exist_ok=True,ignore=shutil.ignore_patterns('*.meta'))
        if not (staged/'resource_profile.json').exists():
            (staged/'resource_profile.json').write_text(json.dumps(dict(schema=1,kind='external-full',notice='Locally imported; upstream licenses unchanged.'))+'\n')
        if output.exists():
            backup=output.with_name(output.name+f'.backup-{time.time_ns()}');os.replace(output,backup)
        try:os.replace(staged,output)
        except Exception:
            if backup is not None:os.replace(backup,output)
            raise
    finally:
        if staged.exists():shutil.rmtree(staged)
    return dict(result,backup=str(backup) if backup else None)


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source',required=True,type=Path)
    p.add_argument('--output',type=Path,default=ROOT/'unity/Assets/StreamingAssets/replay')
    p.add_argument('--replace-demo',action='store_true');p.add_argument('--dry-run',action='store_true')
    a=p.parse_args();result=inspect(a.source) if a.dry_run else install(a.source,a.output,a.replace_demo)
    print(json.dumps(result,ensure_ascii=False))


if __name__=='__main__':main()
