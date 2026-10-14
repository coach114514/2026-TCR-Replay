"""Full compressed-library integrity and precomputed-tag audit."""
import argparse
import gzip
import hashlib
import json
from pathlib import Path
from .cli import validate,connect,write_json,ROOT


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--folder',type=Path,default=ROOT/'unity/Assets/StreamingAssets/replay')
    parser.add_argument('--database',type=Path,default=Path.home()/'.cache/tcr-replay/rmuc_2026_region_dataset.sqlite')
    args=parser.parse_args();folder=args.folder
    catalog=json.loads((folder/'catalog.json').read_text());states=events=shots=0;identities=set();bookmarks=0
    required={f'T{i:02}' for i in range(1,51)}|{'S01','S02'}
    for index,entry in enumerate(catalog['matches']):
        packed=(folder/entry['file']).read_bytes()
        assert hashlib.sha256(packed).hexdigest()==entry['sha256'],entry['file']
        payload=json.loads(gzip.decompress(packed));validate(payload)
        for field in ('gameId','region','matchNumber','round','redSchool','blueSchool','duration'):
            assert entry[field]==payload[field],(entry['gameId'],field)
        identity=(entry['region'],entry['matchNumber'],entry['round'])
        assert identity not in identities,identity;identities.add(identity)
        assert required<={tag['id'] for tag in entry['tags']}
        assert not payload['mechanisms']['baseArmor']
        assert entry['endReason']=='unknown'
        for tag in entry['tags']:
            assert tag['status'] in (-1,0,1) and -1<=tag['t']<=payload['duration']
            assert not tag['id'].startswith('C') or tag['status']!=1
        for point in entry['bookmarks']:
            assert 0<=point['t']<=payload['duration'] and point['evidence'];bookmarks+=1
        states+=sum(len(e['samples']) for e in payload['entities']);events+=len(payload['events'])
        shots+=sum(e['kind']=='发弹' and e['category'] in ('17mm','42mm') for e in payload['events'])
        if(index+1)%100==0:print('LIBRARY_VALIDATE_PROGRESS',index+1,flush=True)
    expected={'T01':182,'T02':423,'T03':8,'T05':261,'T07':11,'T09':228,'T12':21,'T13':16,'T17':13,'T19':228,'T20':83,'T29':374,'T31':178,'T32':15,'T33':108,'T36':234,'T47':254}
    counts={code:sum(any(t['id']==code and t['status']==1 for t in m['tags']) for m in catalog['matches']) for code in expected}
    assert counts==expected,counts
    conn=connect(args.database)
    source_counts=tuple(conn.execute('SELECT COUNT(*) FROM '+table).fetchone()[0] for table in ('matches','timeseries','events'));conn.close()
    assert (len(identities),states,events)==source_counts==(613,4015383,1474178)
    old=json.loads((folder/'catalog-13-backup.json').read_text())
    assert all(hashlib.sha256((folder/p).read_bytes()).hexdigest()==value for p,value in old['hashes'].items())
    report=dict(matches=len(identities),states=states,events=events,shots=shots,bookmarks=bookmarks,
                sha256Verified=True,identityVerified=True,tagDistributionChecks=counts,baseArmorUnknown=True,original13Unchanged=True,sourceReadOnly=True)
    write_json(ROOT/'artifacts/library/library-validation.json',report)
    print('LIBRARY_VALIDATE_OK '+json.dumps(report,ensure_ascii=False),flush=True)


if __name__=='__main__':main()
