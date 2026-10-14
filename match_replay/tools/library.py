"""Per-game compression, evidence-based summaries and a small searchable catalog.

Original SQLite and existing 13 JSON files stay read-only. No visual inference
enters recorded statistics. Tag status: 1=yes, 0=no in source, -1=unknown.
"""
from __future__ import annotations
from collections import Counter, defaultdict
import gzip
import hashlib
import json
import math
from pathlib import Path
import re
import time
from .cli import build_match, write_json

TAG_NAMES = [
    '单方前哨被毁','双方前哨被毁','双方前哨记录中存活','己方先推前哨','120秒内破前哨',
    '双方破前哨时间差大','前哨重建','重建后再被毁','双方基地未失血','单方基地失血',
    '双方基地失血','基地≤1000','基地≤500','低血基地获胜','先失前哨获胜',
    '基地显著回血','提前结束','基地血量落后获胜','小机关成功','大机关成功',
    '大小机关都成功','多次机关成功','双方同类机关成功','机关进度回退','30秒内小机关成功',
    '大机关高环击中','最高装配等级1','最高装配等级2','三级装配完成','多次一级装配',
    '飞镖命中基地','飞镖命中前哨','多次飞镖命中','单方多次飞镖命中','空中发弹占比高',
    '雷达反制空中','英雄42mm发弹多','3号地面发弹主导','4号地面发弹主导','哨兵地面发弹主导',
    '10秒发弹密集','60秒内地面战亡','同一机器人多次复活','多车同时战亡','30秒战亡集中',
    '撞击扣血多','判罚扣血','累计金币高','金币差反超','末段金币留存高']
DEFAULT_IDS = {f'T{i:02}' for i in [*range(1,16),17,*range(19,24),29,*range(31,37),41,47]}


def tag(tags, n, status, team='', t=-1, value=0, evidence='源记录统计'):
    tags.append(dict(id=f'T{n:02}', team=team, status=int(status), t=t,
                     value=value, evidence=evidence))


def complete(samples, duration):
    return bool(samples and samples[0]['t'] <= 1 and samples[-1]['t'] >= duration-1
                and all(s['hasHp'] for s in samples)
                and all(b['t']-a['t'] <= 2 for a,b in zip(samples,samples[1:])))


def changes(samples):
    deaths, respawns, heal = [], [], 0
    active = False
    for s in samples:
        if s['hasHp'] and s['hp'] > 0:
            active = True
            break
    seen_alive = False
    for a,b in zip(samples,samples[1:]):
        if a['hasHp'] and a['hp'] > 0: seen_alive = True
        if not (a['hasHp'] and b['hasHp']) or b['t']-a['t'] > 2: continue
        if a['hp'] > 0 and b['hp'] <= 0: deaths.append(b['t'])
        if seen_alive and a['hp'] <= 0 and b['hp'] > 0: respawns.append(b['t'])
        if b['hp'] > a['hp']: heal += b['hp']-a['hp']
    return deaths, respawns, heal, active


def rune_progress(e):
    typ = re.search(r'rune_type=([0-9.]+)',e['category'])
    arms = re.search(r'arm_cnt=([0-9.]+)',e['note'])
    avg = re.search(r'avg_round=([0-9.]+)',e['note'])
    if not typ or not arms: return None
    raw = float(typ[1])
    if raw not in (0,1): return None
    return dict(t=e['t'], team=e['team'], kind='small' if raw==1 else 'large',
                arms=int(float(arms[1])), rings=e['value'] if e['hasValue'] else -1,
                average=float(avg[1]) if avg else -1, evidence=e['id'])


def mechanisms(payload):
    progress, success = [], {}
    for e in payload['events']:
        if e['kind']=='能量机关':
            p=rune_progress(e)
            if p: progress.append(p)
        if e['kind']=='增益' and e['category'] in ('小能量机关增益','大能量机关增益'):
            kind='small' if e['category']=='小能量机关增益' else 'large'
            key=(e['team'],kind,e['t'])
            success.setdefault(key,dict(t=e['t'],team=e['team'],kind=kind,
                                       state='activated',ends=e['t']+45 if kind=='small' else -1,
                                       evidence=e['id'],arms=-1))
    # A progress row is an observation, not proof of a complete attempt or of
    # its final arm count. Large success expiry deliberately remains unknown.
    return dict(schema=1,baseArmor=[],outpost=[],runeProgress=progress,
                runeSuccess=list(success.values()),rules='V2.0.1 display / source regional version unverified')


def max_run(values, predicate):
    longest = run = 0
    start = best = -1
    previous = None
    for t,value in values:
        if predicate(value):
            if previous is None or t-previous > 1.01 or run == 0: start=t; run=1
            else: run+=t-previous
            if run>longest: longest=run;best=start
        else: run=0
        previous=t
    return longest,best


def summary(payload, row):
    entities={e['id']:e for e in payload['entities']}
    duration=payload['duration']; tags=[]; bookmarks=[]; stats=[]
    building={}; movement={}; shots=Counter(); shot_times=defaultdict(list)
    for e in payload['events']:
        if e['kind']=='发弹':
            shots[(e['team'],e['entity'].split('_',1)[-1],e['category'])]+=1
            shot_times[e['team']].append(e['t'])
    for team in ('red','blue'):
        for role in ('base','outpost'):
            samples=entities.get(f'{team}_{role}',{}).get('samples',[])
            hp=[s['hp'] for s in samples if s['hasHp']]
            deaths,respawns,heal,active=changes(samples)
            loss=any(s['hasHp'] and s['hasMaxHp'] and s['hp']<s['maxHp'] for s in samples)
            zero=next((s['t'] for s in samples if s['hasHp'] and s['hp']<=0),-1)
            building[(team,role)]=dict(samples=samples,full=complete(samples,duration),
                                      loss=loss,min=min(hp) if hp else -1,death=zero,
                                      respawns=respawns,deaths=deaths,heal=heal)
            if zero>=0: bookmarks.append(dict(t=zero,team=team,name='前哨首次归零' if role=='outpost' else '基地归零',evidence='血量状态'))
        for role in ('hero','engineer','infantry3','infantry4','sentry'):
            samples=entities.get(f'{team}_{role}',{}).get('samples',[])
            movement[(team,role)] = changes(samples)
    out=[building[(t,'outpost')] for t in ('red','blue')]
    base=[building[(t,'base')] for t in ('red','blue')]
    nout=sum(b['death']>=0 for b in out); nloss=sum(b['loss'] for b in base)
    allout=all(b['full'] for b in out); allbase=all(b['full'] for b in base)
    tag(tags,1,nout==1 if allout else -1)
    tag(tags,2,1 if nout==2 else 0 if allout else -1)
    tag(tags,3,nout==0 if allout else -1)
    first=min([b['death'] for b in out if b['death']>=0],default=-1)
    tag(tags,5,1 if 0<=first<=120 else 0 if allout else -1,t=first,value=first)
    tag(tags,6,abs(out[0]['death']-out[1]['death'])>=60 if nout==2 else 0 if allout else -1,
        value=abs(out[0]['death']-out[1]['death']) if nout==2 else -1)
    tag(tags,9,nloss==0 if allbase else -1)
    tag(tags,10,nloss==1 if allbase else -1)
    tag(tags,11,1 if nloss==2 else 0 if allbase else -1)
    tag(tags,17,duration<420,t=duration,value=duration,evidence='源对局时长；结束原因未知')
    mech=mechanisms(payload); payload['mechanisms']=mech
    winner={'红':'red','蓝':'blue'}.get(payload['winner'],'')
    for team,other in (('red','blue'),('blue','red')):
        b=building[(team,'base')]; o=building[(team,'outpost')]; enemy=building[(other,'outpost')]
        earlier=enemy['death']>=0 and (o['death']<0 or enemy['death']<o['death'])
        tag(tags,4,1 if earlier else 0 if allout else -1,team,t=enemy['death'])
        tag(tags,7,bool(o['respawns']) if o['full'] else 1 if o['respawns'] else -1,team,t=o['respawns'][0] if o['respawns'] else -1)
        rebuilt_dead=next((t for t in o['deaths'] if o['respawns'] and t>o['respawns'][0]),-1)
        tag(tags,8,1 if rebuilt_dead>=0 else 0 if o['full'] else -1,team,t=rebuilt_dead)
        for n,threshold in ((12,1000),(13,500)):
            time_low=next((s['t'] for s in b['samples'] if s['hasHp'] and s['hp']<=threshold),-1)
            tag(tags,n,1 if time_low>=0 else 0 if b['full'] else -1,team,t=time_low,value=b['min'])
        tag(tags,14,team==winner and 0<=b['min']<=1000 if b['full'] else -1,team)
        tag(tags,15,team==winner and o['death']>=0 and (enemy['death']<0 or o['death']<enemy['death']) if allout else -1,team,t=o['death'])
        tag(tags,16,b['heal']>=500 if b['full'] else 1 if b['heal']>=500 else -1,team,value=b['heal'])
        bh={s['t']:s['hp'] for s in b['samples'] if s['hasHp']}
        eh={s['t']:s['hp'] for s in building[(other,'base')]['samples'] if s['hasHp']}
        lag,time_lag=max_run([(t,bh[t]-eh[t]) for t in sorted(bh.keys()&eh.keys())],lambda v:v<=-1000)
        tag(tags,18,team==winner and lag>=30 if allbase else -1,team,t=time_lag,value=lag)
        success=[s for s in mech['runeSuccess'] if s['team']==team]
        small=[s for s in success if s['kind']=='small'];large=[s for s in success if s['kind']=='large']
        for n,items in ((19,small),(20,large)):
            tag(tags,n,bool(items),team,t=min((s['t'] for s in items),default=-1),value=len(items))
        tag(tags,21,bool(small and large),team)
        tag(tags,22,len(success)>=2,team,value=len(success))
        enemy_success={s['kind'] for s in mech['runeSuccess'] if s['team']==other}
        tag(tags,23,bool({s['kind'] for s in success}&enemy_success),team)
        progress=[p for p in mech['runeProgress'] if p['team']==team]
        retreats=[q for p,q in zip(progress,progress[1:]) if p['kind']==q['kind'] and q['t']-p['t']<=20 and q['arms']<p['arms']]
        tag(tags,24,1 if retreats else -1,team,t=retreats[0]['t'] if retreats else -1,evidence='邻近计数回退；尝试边界和失败原因未知')
        tag(tags,25,any(s['t']<=30 for s in small),team,t=min((s['t'] for s in small),default=-1))
        high=[p for p in progress if p['kind']=='large' and p['rings']>=8]
        tag(tags,26,bool(high),team,t=high[0]['t'] if high else -1)
        assemblies=[e for e in payload['events'] if e['team']==team and e['kind']=='装配成功']
        levels=[int(re.search(r'等级(\d+)',e['category'])[1]) for e in assemblies if re.search(r'等级(\d+)',e['category'])]
        maximum=max(levels,default=0)
        for n,level in ((27,1),(28,2),(29,3)): tag(tags,n,maximum==level,team,value=maximum)
        level1=[e for e in assemblies if e['category']=='等级1']
        tag(tags,30,len(level1)>=2,team,value=len(level1))
        # Dart hit events identify the victim building through robot_id/type;
        # firing team is event team, and no target-mode subtype is available.
        darts=[e for e in payload['events'] if e['team']==team and e['kind']=='飞镖命中']
        for n,role in ((31,'base'),(32,'outpost')):
            items=[e for e in darts if e['targetType']=={'base':'基地','outpost':'前哨站'}[role] or e['entity'].endswith('_'+role)]
            tag(tags,n,bool(items),team,t=items[0]['t'] if items else -1,value=len(items),evidence='飞镖命中事件的明确目标类型')
        tag(tags,34,len(darts)>=2,team,t=darts[0]['t'] if darts else -1,value=len(darts))
        total17=sum(v for (t,role,caliber),v in shots.items() if t==team and caliber=='17mm')
        air=shots[(team,'air','17mm')];ground17=sum(shots[(team,r,'17mm')] for r in ('infantry3','infantry4','sentry'))
        tag(tags,35,total17>0 and air/total17>=.35,team,value=air/total17 if total17 else 0)
        counters=[e for e in payload['events'] if e['kind']=='雷达反制UAV' and ('反制方='+('红' if team=='red' else '蓝')) in e['note']]
        tag(tags,36,bool(counters),team,t=counters[0]['t'] if counters else -1,value=len(counters))
        for n,role in ((38,'infantry3'),(39,'infantry4'),(40,'sentry')):
            ratio=shots[(team,role,'17mm')]/ground17 if ground17 else 0
            tag(tags,n,ground17>0 and ratio>=.5,team,value=ratio)
        all_deaths=sorted(t for role in ('hero','infantry3','infantry4','sentry') for t in movement[(team,role)][0])
        tag(tags,42,bool(all_deaths and all_deaths[0]<=60),team,t=all_deaths[0] if all_deaths else -1)
        respawns=max((len(movement[(team,r)][1]) for r in ('hero','engineer','infantry3','infantry4','sentry')),default=0)
        tag(tags,43,respawns>=2,team,value=respawns)
        dead_count=Counter()
        for role in ('hero','infantry3','infantry4','sentry'):
            ent=entities.get(team+'_'+role)
            if not ent or not movement[(team,role)][3]:continue
            alive=False
            for s in ent['samples']:
                if s['hasHp'] and s['hp']>0:alive=True
                if alive and s['hasHp'] and s['hp']<=0:dead_count[s['t']]+=1
        dead_run,dead_t=max_run(sorted(dead_count.items()),lambda n:n>=2)
        tag(tags,44,dead_run>=5,team,t=dead_t,value=dead_run)
        clusters=max((sum(t<=u<t+30 for u in all_deaths) for t in all_deaths),default=0)
        tag(tags,45,clusters>=3,team,value=clusters)
        penalties=[e for e in payload['events'] if e['team']==team and e['category']=='判罚']
        tag(tags,47,bool(penalties),team,t=penalties[0]['t'] if penalties else -1,value=sum(abs(e['value']) for e in penalties))
        coins={}
        for ent in entities.values():
            if ent['team']!=team:continue
            for s in ent['samples']:
                if s['hasCoins']:coins.setdefault(s['t'],(s['totalCoins'],s['coins']))
        last=max(coins,default=-1);endcoins=coins.get(last,(-1,-1))
        st=shot_times[team];left=0;burst=0;burst_time=-1
        for right,t in enumerate(st):
            while t-st[left]>=10:left+=1
            if right-left+1>burst:burst=right-left+1;burst_time=st[left]
        collision=sum(abs(e['value']) for e in payload['events'] if e['team']==team and e['category']=='撞击')
        stats.append(dict(team=team,minBase=b['min'],outpostAt=o['death'],hero42=shots[(team,'hero','42mm')],
                          shots17=total17,shots42=sum(v for (t,r,c),v in shots.items() if t==team and c=='42mm'),
                          shotPeak=burst,shotPeakAt=burst_time,collisionPerMinute=collision*60/duration,
                          totalCoins=endcoins[0],coins=endcoins[1],coinsAt=last,
                          deaths=len(all_deaths),respawns=respawns,runeSmall=len(small),runeLarge=len(large),darts=len(darts)))
    all_darts=[e for e in payload['events'] if e['kind']=='飞镖命中']
    tag(tags,33,len(all_darts)>=2,value=len(all_darts))
    # Coin lead is a team value sampled once per second, never summed over units.
    team_coins={}
    for team in ('red','blue'):
        bytime={}
        for ent in entities.values():
            if ent['team']==team:
                for s in ent['samples']:
                    if s['hasCoins']:bytime.setdefault(s['t'],s['totalCoins'])
        team_coins[team]=bytime
    common=sorted(team_coins['red'].keys()&team_coins['blue'].keys())
    for team,other in (('red','blue'),('blue','red')):
        values=[(t,team_coins[team][t]-team_coins[other][t]) for t in common]
        lag,lag_t=max_run(values,lambda x:x<=-100)
        lead,lead_t=max_run([(t,v) for t,v in values if t>=lag_t+10],lambda x:x>=100)
        tag(tags,49,lag>=10 and lead>=10,team,t=lead_t)
    for s in mech['runeSuccess']:bookmarks.append(dict(t=s['t'],team=s['team'],name=('小' if s['kind']=='small' else '大')+'机关成功',evidence=s['evidence']))
    for e in payload['events']:
        if e['kind'] in ('装配成功','飞镖命中','雷达反制UAV'):
            bookmarks.append(dict(t=e['t'],team=e['team'],name=e['kind']+' '+e['category'],evidence=e['id']))
    return dict(gameId=payload['gameId'],webGameId=str(row['web_game_id']),seriesKey=f'{row["赛区"]}:{row["场次号"]}',
                region=row['赛区'],matchNumber=row['场次号'],round=row['局号'],redSchool=row['红方学校'],blueSchool=row['蓝方学校'],
                winner=row['胜方'] or '',startTime=row['开始时间'],duration=duration,states=sum(len(e['samples']) for e in entities.values()),
                events=len(payload['events']),tags=tags,metrics=stats,bookmarks=sorted(bookmarks,key=lambda p:p['t']),
                quality=payload['quality'],ruleVersion='区域赛源版本待核实',endReason='unknown')


def percentile_tags(entries):
    for region in {e['region'] for e in entries}:
        peers=[e for e in entries if e['region']==region]
        for n,key,percentile in ((37,'hero42',.75),(41,'shotPeak',.90),(46,'collisionPerMinute',.75),(48,'totalCoins',.75),(50,'coins',.75)):
            values=sorted(m[key] for e in peers for m in e['metrics'] if m[key]>=0)
            cutoff=values[min(len(values)-1,math.ceil(len(values)*percentile)-1)] if values else math.inf
            for e in peers:
                for m in e['metrics']:
                    tag(e['tags'],n,m[key]>0 and m[key]>=cutoff if m[key]>=0 else -1,m['team'],
                        t=m.get('shotPeakAt',-1) if n==41 else m.get('coinsAt',-1) if n==50 else -1,
                        value=m[key],evidence=f'同赛区{percentile:.0%}分位阈值={cutoff:g}')


def school_aliases(schools):
    # Dependency is isolated in the project's cache, not installed system-wide.
    import sys
    dependency=Path.home()/'.cache/tcr-replay/python'
    if dependency.is_dir():sys.path.insert(0,str(dependency))
    try:
        from pypinyin import lazy_pinyin
    except ImportError:
        raise RuntimeError('导出学校拼音需要pypinyin；请安装到~/.cache/tcr-replay/python')
    result=[]
    for name in sorted(schools):
        syllables=lazy_pinyin(name)
        aliases=[name,''.join(syllables),''.join(s[0] for s in syllables)]
        for suffix in ('大学','学院'):
            if name.endswith(suffix):aliases.append(name[:-len(suffix)])
        special={'厦门大学嘉庚学院':['嘉庚','厦大嘉庚','jiageng','jg'], '国防科技大学':['国防科大','国防科技','nudt','gfkd']}
        aliases.extend(special.get(name,[]))
        result.append(dict(name=name,aliases=list(dict.fromkeys(aliases))))
    return result


def export_library(conn, database, output):
    start=time.monotonic(); output.mkdir(parents=True,exist_ok=True)
    folder=output/'library';folder.mkdir(exist_ok=True)
    entries=[];hashes={};states=events=raw_bytes=compressed_bytes=0
    rows=conn.execute('SELECT * FROM matches ORDER BY 开始时间,赛区,场次号,局号').fetchall()
    unique=set();series=defaultdict(list)
    for i,row in enumerate(rows):
        identity=(row['赛区'],row['场次号'],row['局号'])
        if identity in unique:raise ValueError(f'重复战局: {identity}')
        unique.add(identity)
        payload,_=build_match(conn,row)
        entry=summary(payload,row)
        raw=(json.dumps(payload,ensure_ascii=False,allow_nan=False,separators=(',',':'))+'\n').encode('utf-8')
        zipped=gzip.compress(raw,compresslevel=5,mtime=0)
        filename=f'library/{payload["gameId"]}.json.gz'
        path=output/filename;temp=path.with_suffix('.tmp');temp.write_bytes(zipped);temp.replace(path)
        entry['file']=filename;entry['sha256']=hashlib.sha256(zipped).hexdigest();entry['compressedBytes']=len(zipped)
        hashes[filename]=entry['sha256'];entries.append(entry);series[entry['seriesKey']].append(entry)
        states+=entry['states'];events+=entry['events'];raw_bytes+=len(raw);compressed_bytes+=len(zipped)
        if (i+1)%25==0 or i+1==len(rows):print(f'LIBRARY_PROGRESS {i+1}/{len(rows)} states={states} events={events}',flush=True)
    percentile_tags(entries)
    pairs=Counter(tuple(sorted((items[0]['redSchool'],items[0]['blueSchool']))) for items in series.values())
    for items in series.values():
        items.sort(key=lambda e:e['round'])
        winners=[e['redSchool'] if e['winner']=='红' else e['blueSchool'] if e['winner']=='蓝' else '' for e in items]
        comeback=len(winners)>=3 and winners[0] and winners[1]==winners[2] and winners[1]!=winners[0]
        rematch=pairs[tuple(sorted((items[0]['redSchool'],items[0]['blueSchool'])))]>1
        for e in items:
            for code,yes,note in [('S01',comeback,'已记录首局失利后连胜两局'),('S02',rematch,'不同场次的同校对阵')]:
                e['tags'].append(dict(id=code,status=int(bool(yes)),team='',t=-1,value=0,evidence=note))
    schools=school_aliases({e[t] for e in entries for t in ('redSchool','blueSchool')})
    definitions=[dict(id=f'T{i:02}',name=name,default=f'T{i:02}' in DEFAULT_IDS) for i,name in enumerate(TAG_NAMES,1)]
    definitions.extend([dict(id='S01',name='首局失利后连胜两局',default=False),dict(id='S02',name='不同场次再次交锋',default=False)])
    definitions.extend(dict(id=f'C{i:02}',name=name,default=False,requiresMoreData=True) for i,name in enumerate([
        '确认爆基地/结束原因','基地护甲展开','飞镖目标子类型/开甲','英雄部署','真实命中率/击杀/伤害贡献','校准路线','占领状态','机关明确失败/靶臂顺序'],1))
    add_tag_bookmarks(entries,definitions)
    catalog=dict(schema=1,matches=entries,schools=schools,tagDefinitions=definitions,hashes=hashes,
                 sourceDatabase=database.name,sourceSize=database.stat().st_size,sourceMtimeNs=database.stat().st_mtime_ns,
                 source='all_official_regional',series=len(series))
    old=output/'catalog.json';backup=output/'catalog-13-backup.json'
    if old.exists() and not backup.exists():backup.write_bytes(old.read_bytes())
    write_json(output/'catalog.next.json',catalog);(output/'catalog.next.json').replace(old)
    report=dict(matches=len(entries),series=len(series),schools=len(schools),states=states,events=events,
                rawBytes=raw_bytes,compressedBytes=compressed_bytes,seconds=round(time.monotonic()-start,3),
                tagCounts={d['id']:sum(any(t['id']==d['id'] and t['status']==1 for t in e['tags']) for e in entries) for d in definitions},
                sourceReadOnly=True,baseArmor='interface_only_unknown',endReason='unknown')
    write_json(Path(__file__).resolve().parents[1]/'artifacts/library/export-report.json',report)
    print('LIBRARY_EXPORT_OK '+json.dumps(report,ensure_ascii=False),flush=True)


def add_tag_bookmarks(entries,definitions):
    """Expose time-bearing computed tags as reviewable jump points."""
    names={d['id']:d['name'] for d in definitions}
    for entry in entries:
        points=entry['bookmarks']
        existing={(p['name'],p['team'],p['t']) for p in points}
        for item in entry['tags']:
            if item['status']!=1 or item['t']<0:continue
            name=item['id']+' '+names[item['id']]
            identity=(name,item['team'],item['t'])
            if identity not in existing:
                points.append(dict(name=name,team=item['team'],t=item['t'],evidence=item['evidence']))
                existing.add(identity)
        points.sort(key=lambda p:p['t'])
