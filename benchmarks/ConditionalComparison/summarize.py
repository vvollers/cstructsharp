import json, statistics, pathlib, sys
sys.stdout.reconfigure(encoding="utf-8")
p=pathlib.Path('agentdocs/benchmark-results')
data={}
use_stabilized = '--stabilized-header' in sys.argv
for f in list(p.glob('*.json')) + (list((p/'stabilized-header').glob('*.json')) if use_stabilized else []):
 if not isinstance(json.loads(f.read_text()),list):continue
 for row in json.loads(f.read_text()):
  if f.parent == p and f.stem.startswith('native-') and f.stem.rsplit('-',1)[0] in ['native-main','native-branch'] and row['scenario']=='header' and row['operation']=='compile' and use_stabilized: continue
  key=(f.stem.rsplit('-',1)[0],row['scenario'],row['operation'])
  data.setdefault(key,[]).append(row)
lines=['| Runtime | Scenario | Operation | Main µs | Branch µs | Change | Main → branch bytes/op |','|---|---|---|---:|---:|---:|---:|']
for runtime in ['native','js']:
 for (_,scenario,op),rows in data.items():
  if _ != runtime+'-main':continue
  other=data.get((runtime+'-branch',scenario,op))
  if not other:continue
  a=statistics.median([statistics.median(r['ns']) for r in rows]);b=statistics.median([statistics.median(r['ns']) for r in other])
  alloc='—' if 'allocated' not in rows[0] else f"{statistics.median(rows[0]['allocated']):,.0f} → {statistics.median(other[0]['allocated']):,.0f}"
  lines.append(f'| {runtime} | {scenario} | {op} | {a/1000:.2f} | {b/1000:.2f} | {(b/a-1)*100:+.1f}% | {alloc} |')
lines+=['','| Runtime | Records | Operation | Plain µs | If µs (ratio) | Switch µs (ratio) |','|---|---:|---|---:|---:|---:|']
for runtime in ['native','js']:
 for count in [1,128]:
  for op in (['compile','parse','debug','span'] if runtime=='native' else ['compile','parseCore','parseJson','publicParse','publicDebug']):
   nums=[]
   for mode in ['plain','if','switch']:
    rows=data.get((runtime+'-branch',mode+str(count),op))
    if rows: nums.append(statistics.median([statistics.median(r['ns']) for r in rows])/1000)
   if len(nums)==3:
    a,b,c=nums
    lines.append(f'| {runtime} | {count} | {op} | {a:.2f} | {b:.2f} ({b/a:.2f}×) | {c:.2f} ({c/a:.2f}×) |')
(p/'tables.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print('\n'.join(lines))
