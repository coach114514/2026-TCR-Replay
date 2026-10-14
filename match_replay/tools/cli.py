"""Read-only SQLite export; full-library school aliases use pypinyin."""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import hashlib
import gzip
import json
import math
from pathlib import Path
import shutil
import sqlite3
import struct
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
WORKSPACE = ROOT.parent
DEFAULT_ARCHIVE = None
ROLE_MAP = {'英雄':'hero', '工程':'engineer', '步兵3':'infantry3', '步兵4':'infantry4',
            '哨兵':'sentry', '空中':'air', '基地':'base', '前哨站':'outpost'}


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, allow_nan=False,
                               separators=(',', ':')) + '\n', encoding='utf-8')


def database_path(args):
    if args.database:
        return Path(args.database).expanduser().resolve()
    if not args.archive:
        raise ValueError('Specify --database PATH or --archive PATH; official data is not bundled.')
    archive = Path(args.archive).expanduser().resolve()
    if not archive.is_file():
        raise ValueError(f'找不到数据集: {archive}')
    cache = Path(args.cache).expanduser().resolve()
    cache.mkdir(parents=True, exist_ok=True)
    stamp = f'{archive.stat().st_size}:{archive.stat().st_mtime_ns}'
    target = cache / 'rmuc_2026_region_dataset.sqlite'
    marker = cache / 'archive_stamp.txt'
    if not (target.is_file() and marker.is_file() and marker.read_text() == stamp):
        executable = shutil.which('bsdtar')
        if not executable:
            raise ValueError('Install bsdtar (libarchive-tools), or extract the archive yourself and use --database.')
        # Extract only the known member into a temporary directory. Partial
        # extraction never replaces a previously usable cached database.
        with tempfile.TemporaryDirectory(dir=cache) as temporary:
            subprocess.run([executable, '-xf', str(archive), '-C', temporary,
                            'rmuc_2026_region_dataset.sqlite'], check=True)
            extracted = Path(temporary) / target.name
            with extracted.open('rb') as handle:
                if handle.read(16) != b'SQLite format 3\x00':
                    raise ValueError('解压文件不是 SQLite 数据库')
            extracted.replace(target)
        marker.write_text(stamp)
    return target


def connect(path):
    conn = sqlite3.connect(Path(path).as_uri() + '?mode=ro', uri=True)
    conn.row_factory = sqlite3.Row
    return conn


def finite(value):
    return value is not None and math.isfinite(float(value))


def number(value):
    return float(value) if finite(value) else 0.0


def encode_sample(row, role):
    building = role in ('base', 'outpost')
    x, y = row['x'], row['y']
    flags = []
    if building:
        flags.append('map_anchor_provisional')
    elif not finite(x) or not finite(y):
        flags.append('missing_xy')
    elif x == 0 and y == 0:
        flags.append('zero_xy')
    elif not (0 <= x <= 28 and 0 <= y <= 15):
        flags.append('outside_candidate_field')
    if finite(row['z']) and row['z'] < 0:
        flags.append('negative_recorded_z')
    if role == 'air':
        flags.append('flight_state_unknown')
    return {
        't': number(row['时刻秒']), 'x':number(x), 'y':number(y), 'z':number(row['z']),
        'valid': not any(f in flags for f in ('missing_xy','zero_xy','outside_candidate_field')) and not building,
        'gun':number(row['枪口朝向']), 'hasGun': finite(row['枪口朝向']) and role not in ('base','outpost','engineer'),
        'hp':number(row['当前血量']), 'hasHp':finite(row['当前血量']),
        'maxHp':number(row['最大血量']), 'hasMaxHp':finite(row['最大血量']),
        'power':number(row['底盘功率']), 'hasPower':finite(row['底盘功率']),
        'heat17':number(row['小热量']), 'heat42':number(row['大热量']),
        'hasHeat17':finite(row['小热量']), 'hasHeat42':finite(row['大热量']),
        'ammo17':number(row['累计17mm发弹']), 'hasAmmo17':finite(row['累计17mm发弹']),
        'ammo42':number(row['累计42mm发弹']), 'hasAmmo42':finite(row['累计42mm发弹']),
        'coins':number(row['队伍剩余金币']), 'hasCoins':finite(row['队伍剩余金币']),
        'totalCoins':number(row['队伍总金币']), 'marked': bool(row['是否易伤']),
        'flags':flags, 'row':int(row['source_row'])
    }


def build_match(conn, match):
    game_id = int(match['game_id'])
    entities = {}
    quality = Counter()
    raw_count = 0
    rows = conn.execute('SELECT rowid AS source_row,* FROM timeseries WHERE game_id=? '
                        'ORDER BY 时刻秒,rowid', (game_id,))
    for row in rows:
        raw_count += 1
        role = ROLE_MAP.get(row['机器人类型'])
        if role is None:
            raise ValueError(f'未知实体类型: {row["机器人类型"]}')
        team = {'红':'red', '蓝':'blue'}[row['阵营']]
        key = f'{team}_{role}'
        entity = entities.setdefault(key, {'id':key, 'robotId':row['robot_id'],
            'name':row['机器人类型'], 'role':role, 'team':team,
            'school':row['学校名'], 'samples':[]})
        sample = encode_sample(row, role)
        if entity['samples'] and sample['t'] == entity['samples'][-1]['t']:
            raise ValueError(f'重复状态键: {game_id}/{key}/{sample["t"]}')
        entity['samples'].append(sample)
        quality.update(sample['flags'])
    events = []
    for row in conn.execute('SELECT rowid AS source_row,* FROM events WHERE game_id=? '
                            'ORDER BY 时刻秒,rowid', (game_id,)):
        team = {'红':'red','蓝':'blue'}.get(row['阵营'], '')
        role = ROLE_MAP.get(row['机器人类型'], '')
        events.append({'id':f'dataset:{game_id}:event:{row["source_row"]}',
            't':number(row['时刻秒']), 'kind':row['事件类型'],
            'entity':f'{team}_{role}' if team and role else '',
            'team':team, 'category':row['类别'] or '',
            'value':number(row['数值']), 'hasValue':finite(row['数值']),
            'targetRobotId':row['目标robot_id'] or 0, 'targetType':row['目标类型'] or '',
            'note':row['备注'] or '', 'row':row['source_row']})
    payload = {'schema':1, 'gameId':str(game_id), 'region':match['赛区'],
        'matchNumber':match['场次号'], 'round':match['局号'],
        'redSchool':match['红方学校'], 'blueSchool':match['蓝方学校'],
        'winner':match['胜方'] or '', 'startTime':match['开始时间'],
        'duration':match['时长秒'], 'timeResolution':1, 'source':'dataset_recorded',
        'fieldId':'rmuc_2026_region_candidate', 'entities':list(entities.values()),
        'events':events, 'quality':[{'flag':k,'count':v} for k,v in sorted(quality.items())]}
    validate(payload)
    return payload, raw_count


def export_match(conn, match, output):
    payload, raw_count = build_match(conn, match)
    game_id = payload['gameId']
    filename = f'matches/{game_id}.json'
    write_json(output / filename, payload)
    return {'gameId':str(game_id), 'file':filename, 'region':match['赛区'],
        'matchNumber':match['场次号'], 'round':match['局号'],
        'redSchool':match['红方学校'], 'blueSchool':match['蓝方学校'],
        'duration':match['时长秒'], 'states':raw_count, 'events':len(payload['events'])}


def validate(payload):
    if payload.get('schema') != 1 or payload.get('duration', 0) <= 0:
        raise ValueError('回放包版本或时长无效')
    ids = set()
    for entity in payload['entities']:
        if entity['id'] in ids:
            raise ValueError('实体身份重复')
        ids.add(entity['id'])
        previous = -math.inf
        for sample in entity['samples']:
            if sample['t'] <= previous or not 0 <= sample['t'] <= payload['duration']:
                raise ValueError('状态时间重复、倒序或超出局范围')
            previous = sample['t']
    event_ids = set()
    previous = -math.inf
    for event in payload['events']:
        if event['id'] in event_ids or event['t'] < previous or not 0 <= event['t'] <= payload['duration']:
            raise ValueError('事件身份或时间无效')
        event_ids.add(event['id'])
        previous = event['t']
    json.dumps(payload, allow_nan=False)


def field_assets(output, map_file=None, mesh_file=None):
    if map_file is None or mesh_file is None:
        if (output / "fields_catalog.json").exists():
            return
        raise ValueError("Prepare a local resource pack first, or supply --map-image and --field-mesh for regional geometry.")
    assets = output / 'assets'
    assets.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(Path(map_file), assets/'map_2026.jpg')
    shutil.copyfile(ROOT/'field_configs/region_2026.json', output/'field_config.json')
    # A fresh regional export is usable on its own; keep an existing official
    # model catalog when exporting match data again.
    if not (output/'fields_catalog.json').exists():
        field = json.loads((output/'field_config.json').read_text())
        write_json(output/'fields_catalog.json', {'schema':1,'defaultField':field['id'],
            'fields':[{'id':field['id'],'name':'原区域赛模型','file':'field_config.json'}]})
    source = Path(mesh_file)
    with source.open('rb') as handle:
        header = []
        while True:
            line = handle.readline().decode('ascii').strip()
            header.append(line)
            if line == 'end_header':
                break
        nv = int(next(line for line in header if line.startswith('element vertex')).split()[-1])
        nf = int(next(line for line in header if line.startswith('element face')).split()[-1])
        positions = []
        for _ in range(nv):
            x,y,z,s,t = struct.unpack('<5f', handle.read(20))
            positions.extend([round(x+14,5),round(z,5),round(y+7.5,5)])
        indices = []
        for _ in range(nf):
            size = handle.read(1)[0]
            face = struct.unpack('<'+'I'*size, handle.read(size*4))
            if size != 3 or any(i >= nv for i in face):
                raise ValueError('场地包含非三角面或无效索引')
            # XY-up to XZ-ground swaps handedness.
            indices.extend([face[0],face[2],face[1]])
    write_json(assets/'region_mesh.json', {'positions':positions,'indices':indices,
        'provisional':True,'source':source.name})


def main():
    parser = argparse.ArgumentParser(description='2026TCR 回放数据导出器')
    parser.add_argument('--database', help='已有SQLite文件；以只读模式打开')
    parser.add_argument('--archive', default=str(DEFAULT_ARCHIVE))
    parser.add_argument('--map-image', type=Path, help='Local regional map image; not redistributed')
    parser.add_argument('--field-mesh', type=Path, help='Local supported regional PLY; not redistributed')
    parser.add_argument('--cache', default=str(Path.home()/'.cache/tcr-replay'))
    sub = parser.add_subparsers(dest='command', required=True)
    listing = sub.add_parser('list-matches')
    listing.add_argument('--school', default='嘉庚')
    exporting = sub.add_parser('export')
    exporting.add_argument('--school', default='嘉庚')
    exporting.add_argument('--game-id', type=int)
    exporting.add_argument('--output', default=str(ROOT/'unity/Assets/StreamingAssets/replay'))
    checking = sub.add_parser('validate')
    checking.add_argument('package', type=Path)
    library = sub.add_parser('export-library', help='导出全部区域赛、压缩数据和搜索索引')
    library.add_argument('--output', default=str(ROOT/'unity/Assets/StreamingAssets/replay'))
    args = parser.parse_args()
    if args.command == 'validate':
        raw = gzip.decompress(args.package.read_bytes()) if args.package.suffix == '.gz' else args.package.read_bytes()
        validate(json.loads(raw))
        print('回放包验证通过')
        return
    path = database_path(args)
    with connect(path) as conn:
        if args.command == 'export-library':
            from match_replay.tools.library import export_library
            export_library(conn, path, Path(args.output).resolve())
            return
        if args.command == 'export' and args.game_id:
            matches = conn.execute('SELECT * FROM matches WHERE game_id=?',(args.game_id,)).fetchall()
        else:
            matches = conn.execute('SELECT * FROM matches WHERE 红方学校 LIKE ? OR 蓝方学校 LIKE ? '
                                   'ORDER BY 开始时间', ('%'+args.school+'%', '%'+args.school+'%')).fetchall()
        if not matches:
            raise ValueError('筛选未找到比赛')
        if args.command == 'list-matches':
            for match in matches:
                print(json.dumps(dict(match),ensure_ascii=False))
            return
        output = Path(args.output).resolve()
        output.mkdir(parents=True,exist_ok=True)
        entries = [export_match(conn, match, output) for match in matches]
        field_assets(output, args.map_image, args.field_mesh)
        # Content hashes make the emitted artifacts traceable. The source
        # database remains external; row references are retained in samples.
        hashes = {entry['file']:hashlib.sha256((output/entry['file']).read_bytes()).hexdigest() for entry in entries}
        write_json(output/'catalog.json', {'schema':1,'matches':entries,'hashes':hashes,
            'sourceDatabase':path.name,'sourceSize':path.stat().st_size,
            'sourceMtimeNs':path.stat().st_mtime_ns})
        print(f'导出 {len(entries)} 局，{sum(x["states"] for x in entries)} 条状态，'
              f'{sum(x["events"] for x in entries)} 条事件 → {output}')


if __name__ == '__main__':
    main()
