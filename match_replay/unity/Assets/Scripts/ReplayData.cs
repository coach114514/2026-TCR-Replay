using System;
using System.IO;
using Unity.SharpZipLib.GZip;
using System.Threading;
using UnityEngine;

namespace TcrReplay
{
    [Serializable] public class MatchCatalog { public int schema,series; public MatchEntry[] matches; public SchoolAliases[] schools; public TagDefinition[] tagDefinitions; }
    [Serializable] public class SchoolAliases {public string name;public string[] aliases;}
    [Serializable] public class TagDefinition {public string id,name;public bool @default,requiresMoreData;}
    [Serializable] public class MatchTag {public string id,team,evidence;public int status;public float t,value;}
    [Serializable] public class MatchBookmark {public string name,team,evidence;public float t;}
    [Serializable] public class MatchQuality {public string flag;public int count;}
    [Serializable] public class TeamMetrics
    {
        public string team;public float minBase,outpostAt,hero42,shots17,shots42,shotPeak,shotPeakAt,collisionPerMinute,totalCoins,coins,coinsAt,deaths,respawns,runeSmall,runeLarge,darts;
    }
    [Serializable] public class MatchEntry
    {
        public string gameId, file, region, redSchool, blueSchool,winner,startTime,seriesKey,webGameId,sha256,ruleVersion,endReason;
        public int matchNumber, round;
        public float duration;public long compressedBytes;public MatchTag[] tags;public TeamMetrics[] metrics;public MatchBookmark[] bookmarks;public MatchQuality[] quality;
    }
    [Serializable] public class MatchData
    {
        public int schema, matchNumber, round;
        public string gameId, region, redSchool, blueSchool, winner, startTime, source, fieldId;
        public float duration, timeResolution;
        public EntityData[] entities;
        public ReplayEvent[] events;
        public MechanismData mechanisms;
    }
    [Serializable] public class EntityData
    {
        public string id, name, role, team, school;
        public int robotId;
        public Sample[] samples;
    }
    [Serializable] public class Sample
    {
        public float t, x, y, z, gun, hp, maxHp, power, heat17, heat42, ammo17, ammo42, coins, totalCoins;
        public bool valid, hasGun, hasHp, hasMaxHp, hasPower, hasHeat17, hasHeat42, hasAmmo17, hasAmmo42, hasCoins, marked;
        public string[] flags;
        public long row;
    }
    [Serializable] public class ReplayEvent
    {
        public string id, kind, entity, team, category, targetType, note;
        public float t, value;
        public bool hasValue;
        public int targetRobotId;
        public long row;
    }
    [Serializable] public class FieldConfig
    {
        public string id, name, calibration, notes, texture, surfaceTexture, mesh, meshFormat, rulesVersion, rulesFigures, coordinateConvention, collisionMesh;
        public float[] textureScaleOffset;
        public float width, depth, meshFloorHeight, meshMaxDisplayHeight;
        public BuildingAnchor[] buildings;
        public BuildingAnchor[] landmarks;
        public PassageRegion[] tunnels;
    }
    [Serializable] public class BuildingAnchor { public string team, role, name, source, modelNode; public float x, y, z; }
    [Serializable] public class PassageRegion
    {
        public string id, name, team, source, calibration;
        public float x, y, halfX, halfY, holdMargin, approachMargin, clearance, floorMax;
    }
    [Serializable] public class RobotCatalog { public int schema; public RobotBinding[] bindings; }
    [Serializable] public class RobotBinding { public string role, model; }
    [Serializable] public class RobotModel
    {
        public int schema, eyeGroup, gunGroup;
        public string id, name, source, sourceSha256, poseProvenance, chassisPose;
        public float defaultFold;
        public float[] eyePoint, labelPoint, yawPivot, pitchPivot, muzzlePoint, gunForward, groundOffsets;
        public float pitchMin, pitchMax;
        public RobotGroup[] groups;
        public RobotArmor[] armorPanels;
        public int[] lodTriangles;
    }
    [Serializable] public class RobotArmor { public float[] position, rotation; }
    [Serializable] public class RobotGroup
    {
        public string name;
        public bool turret, pitch;
        public string[] meshes;
        public float[] pivot, foldRotation, foldTranslation;
    }
    [Serializable] public class FieldMesh { public float[] positions; public int[] indices; }
    [Serializable] public class FieldCatalog { public int schema; public string defaultField; public FieldEntry[] fields; }
    [Serializable] public class FieldEntry { public string id, name, file; }
    [Serializable] public class FieldAssembly { public int schema; public FieldPart[] parts; }
    [Serializable] public class FieldPart
    {
        public string id, name, mesh, binding, sourceProduct, team, colorMode;
        public bool walkable, textured;
        public float[] matrix, color;
    }

    // Queries depend only on the playhead, so seeking backwards never retains future state.
    public static class Timeline
    {
        public static int StateIndex(Sample[] samples, float time)
        {
            int lo = 0, hi = samples.Length;
            while (lo < hi) { int m = (lo + hi) / 2; if (samples[m].t <= time) lo = m + 1; else hi = m; }
            return lo - 1;
        }
        public static int EventEnd(ReplayEvent[] events, float time)
        {
            int lo = 0, hi = events.Length;
            while (lo < hi) { int m = (lo + hi) / 2; if (events[m].t <= time) lo = m + 1; else hi = m; }
            return lo;
        }
        public static bool Pose(EntityData entity, float time, out Vector3 point)
        {
            point = Vector3.zero;
            int i = StateIndex(entity.samples, time);
            if (i < 0) return false;
            Sample a = entity.samples[i];
            if (!a.valid || time - a.t > 2f) return false;
            point = new Vector3(a.x, 0, a.y);
            if (i + 1 < entity.samples.Length)
            {
                Sample b = entity.samples[i + 1];
                if (b.valid && b.t - a.t <= 2f && b.t > a.t)
                    point = Vector3.Lerp(point, new Vector3(b.x, 0, b.y), (time - a.t) / (b.t - a.t));
            }
            return true;
        }
        public static MatchData ReadMatch(string path,CancellationToken cancellation=default)
        {
            cancellation.ThrowIfCancellationRequested();
            string json;
            if(path.EndsWith(".gz",StringComparison.OrdinalIgnoreCase))
                // Use the supported managed decoder with CRC verification. The
                // native Mono inflater intermittently failed on valid packets
                // during repeated full-library reads. Buffers stay request-local.
                using(var stream=new MemoryStream(File.ReadAllBytes(path),false)) using(var gzip=new GZipInputStream(stream))
                using(var reader=new StreamReader(gzip,System.Text.Encoding.UTF8)) json=reader.ReadToEnd();
            else json=File.ReadAllText(path);
            cancellation.ThrowIfCancellationRequested();
            MatchData data = JsonUtility.FromJson<MatchData>(json);
            if (data == null || data.schema != 1 || data.duration <= 0 || data.entities == null || data.events == null)
                throw new InvalidDataException("回放包结构或版本无效");
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (EntityData entity in data.entities)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!ids.Add(entity.id) || entity.samples == null) throw new InvalidDataException("实体身份重复或缺少状态");
                float previous = -1;
                foreach (Sample sample in entity.samples)
                {
                    if (!float.IsFinite(sample.t) || sample.t <= previous || sample.t > data.duration || sample.t < 0)
                        throw new InvalidDataException("状态时间异常");
                    if (!float.IsFinite(sample.x) || !float.IsFinite(sample.y) || !float.IsFinite(sample.z))
                        throw new InvalidDataException("非有限坐标");
                    previous = sample.t;
                }
            }
            ids.Clear(); float lastEvent = -1;
            foreach (ReplayEvent e in data.events)
            {
                if (!ids.Add(e.id) || !float.IsFinite(e.t) || e.t < lastEvent || e.t < 0 || e.t > data.duration)
                    throw new InvalidDataException("事件身份或时间异常");
                lastEvent = e.t;
            }
            return data;
        }
    }
}
