// Rebuild the anonymous, mechanic-only fixtures from original Better Deaths pulls.
import fs from 'node:fs';
import path from 'node:path';
import { gzipSync, gunzipSync } from 'node:zlib';
import { fileURLToPath } from 'node:url';

const source = process.argv[2];
if (!source) throw new Error('Pass the recorded-pull-details directory.');
const dest = fileURLToPath(new URL('../tests/BetterDeaths.Tests/Fixtures/BlackHole/', import.meta.url));
const replayDest = fileURLToPath(new URL('../tests/BetterDeaths.Tests/Fixtures/KefkaReplay/', import.meta.url));
fs.mkdirSync(dest, { recursive: true });
fs.mkdirSync(replayDest, { recursive: true });
for (const n of [2649, 2671, 2673, 2674, 2675, 2681, 2683]) {
  const filename = fs.readdirSync(source).find(f => f.startsWith('pull-' + String(n).padStart(6, '0') + '-'));
  const a = JSON.parse(gunzipSync(fs.readFileSync(path.join(source, filename))));
  const m = a.ReplayMechanics;
  const fires = m.filter(x => x.RawEventKind === 'dmu-p3-nothingness');
  const holes = new Set(fires.map(x => x.SourceKey.split(':')[1]));
  const first = fires[0].PullElapsedSeconds, last = fires.at(-1).PullElapsedSeconds;
  const players = new Map(), entities = new Map();
  const newId = id => {
    if (!entities.has(id)) entities.set(id, (0x40000001 + entities.size).toString(16).toUpperCase());
    return entities.get(id);
  };
  const scrubKey = key => key.split(':').map(s => players.get(s)?.key ??
    (/^[0-9A-F]{8}$/i.test(s) ? newId(s) : s)).join(':');
  for (const p of a.ReplayPositions.filter(x => x.ActorKind === 0)) {
    const key = p.ActorKey.slice(7);
    if (!players.has(key)) players.set(key, { key: String(players.size + 1).padStart(16, '0'), name: 'Player ' + (players.size + 1) });
  }
  const time = '2000-01-01T00:00:00Z';
  const keepMech = x => (x.RawEventKind === 'bossmod-cast' && x.RawEventId === 50167) ||
    ([47846, 47847].includes(x.RawEventId) && x.SourceKey.includes(':cast:')) ||
    (holes.has(x.SourceKey.split(':')[1]) &&
      ['object','black-hole-tether','black-hole-blast','dmu-p3-nothingness'].includes(x.RawEventKind));
  const objects = new Map();
  for (const x of m.filter(x => x.RawEventKind === 'object' && keepMech(x)))
    if (!objects.has(x.SourceKey)) objects.set(x.SourceKey, x);
  const times = [...new Set([...fires, ...objects.values()].map(x=>x.PullElapsedSeconds))].sort((a,b)=>a-b);
  // Keep both sides of every relevant observation, including the late boss turns.
  const around = (records, key, at) => {
    const result = new Set(), groups = Map.groupBy(records,key);
    for (const g of groups.values()) {
      g.sort((a,b)=>a.PullElapsedSeconds-b.PullElapsedSeconds);
      let i=0;
      for (const t of at) {
        while(i<g.length && g[i].PullElapsedSeconds<=t) i++;
        if(i>0) result.add(g[i-1]);
        if(i<g.length) result.add(g[i]);
      }
    }
    return result;
  };
  const tracks = a.ReplayPositions.filter(x => x.PullElapsedSeconds >= first-16 && x.PullElapsedSeconds <= last+2 &&
    (x.ActorKind === 0 || x.ActorName === 'Kefka' && Math.hypot(x.X-100,x.Z-100) <= 3));
  const positions = [...around(tracks, x=>x.ActorKey, times)];
  const tethers = around(m.filter(x=>x.RawEventKind==='black-hole-tether' && keepMech(x)),
    x=>x.SourceKey.split(':')[1], times.flatMap(t=>[t,t+0.30]).sort((a,b)=>a-b));
  const selectedObjects = new Set(objects.values());
  const result = {
    CapturedAtUtc:time, Reason:'Test', PullNumber:n, TerritoryId:1363, TerritoryName:'DMU',
    PullElapsedSeconds:last+2, Deaths:[],
    ReplayPositions:positions.map(p => ({
      ...p, ActorKey:scrubKey(p.ActorKey), ActorName:players.get(p.ActorKey.slice(7))?.name ?? 'Kefka',
      EntityId:parseInt(newId(p.EntityId.toString(16).toUpperCase()),16), SeenAtUtc:time,
    })),
    ReplayMechanics:m.filter(x=>keepMech(x) && (x.RawEventKind!=='object' || selectedObjects.has(x)) &&
      (x.RawEventKind!=='black-hole-tether' || tethers.has(x))).map(x => ({
      ...x, SourceName:x.RawEventKind.includes('hole') || x.RawEventKind==='object' || x.RawEventId===47868 ? 'Black Hole' : 'Kefka',
      SourceKey:scrubKey(x.SourceKey).replace(/:\d{18,}$/, ':0'), Label:'', SeenAtUtc:time,
      RawState:x.RawEventKind==='object' ? parseInt(newId(x.SourceKey.split(':')[1]),16) : x.RawState,
    })),
    ReplayAnalyzerEvents:a.ReplayAnalyzerEvents.filter(x => x.AbilityId===47867).map(x=>({
      ...x, SeenAtUtc:time, SourceEntityId:parseInt(newId(x.SourceEntityId.toString(16).toUpperCase()),16),
    })),
    ReplayDebuffs:a.ReplayDebuffs.filter(x=>[1604,3004,3005,3006].includes(x.Status.Id) && players.has(x.MemberKey)).map(x=>({
      ...x, SeenAtUtc:time, MemberKey:players.get(x.MemberKey).key, MemberName:players.get(x.MemberKey).name,
      Status:{...x.Status,SourceId:0},
    })),
  };
  const json = JSON.stringify(result);
  const sensitive = new Set(a.ReplayPositions.filter(p=>p.ActorKind===0).flatMap(p=>[p.ActorKey.slice(7),p.ActorName]));
  if ([...sensitive].some(s=>s && json.includes(s))) throw new Error('Unredacted player data in fixture '+n);
  fs.writeFileSync(path.join(dest,n+'.json.gz'),gzipSync(json,{level:9}));

  // Unlike the analyzer fixtures, replay needs every facing change between sets
  // and the later stomp sequence. Keep no players, combat log, or ground targets.
  const impacts = m.filter(x => x.RawEventKind === 'dmu-p3-stomp-a-mole');
  const end = Math.max(...impacts.map(x => x.PullElapsedSeconds)) + 3;
  const start = Math.min(...m.filter(x => x.RawEventKind === 'dmu-p3-slap-happy-predicted').map(x => x.PullElapsedSeconds)) - 4;
  const stamp = s => new Date(Date.UTC(2000,0,1) + s * 1000).toISOString();
  const replay = {
    PullNumber:n, TerritoryId:1363, TerritoryName:'DMU', PullElapsedSeconds:end,
    CapturedAtUtc:stamp(end), Reason:'Test', Deaths:[],
    ReplayPositions:a.ReplayPositions.filter(p => p.ActorKind===1 && p.ActorName==='Kefka' &&
      p.PullElapsedSeconds>=start && p.PullElapsedSeconds<=end).map(p => ({
      ...p, ActorKey:scrubKey(p.ActorKey), EntityId:parseInt(newId(p.EntityId.toString(16).toUpperCase()),16),
      SeenAtUtc:stamp(p.PullElapsedSeconds),
    })),
    ReplayMechanics:m.filter(x => [50167,47846,47847,47855,47856,47868].includes(x.RawEventId) &&
      (x.RawEventKind==='bossmod-cast' || ['dmu-p3-slap-happy-predicted','dmu-p3-stomp-a-mole-predicted',
        'dmu-p3-stomp-a-mole','dmu-p3-nothingness'].includes(x.RawEventKind))).map(x => ({
      ...x, SourceKey:scrubKey(x.SourceKey).replace(/:\d{18,}$/, ':0'), RawState:0, SeenAtUtc:stamp(x.PullElapsedSeconds),
    })),
  };
  const replayJson = JSON.stringify(replay);
  if ([...sensitive].some(s=>s && replayJson.includes(s))) throw new Error('Unredacted replay data in fixture '+n);
  fs.writeFileSync(path.join(replayDest,n+'.json.gz'),gzipSync(replayJson,{level:9}));
}
