import json, pathlib, statistics, sys
sys.stdout.reconfigure(encoding='utf-8')
p=pathlib.Path('agentdocs/benchmark-results')
def read(name):
 f=p/(name+'.json')
 return {(r['scenario'],r['operation']):r for r in json.loads(f.read_text())} if f.exists() else {}
def med(r,k='ns'):return statistics.median(r[k])
lines=['| Runtime | Change | Scenario | Operation | Control µs (start–end) | Changed µs | Change vs control midpoint | Bytes/op, control → changed |','|---|---|---|---|---:|---:|---:|---:|']
for rt in ['native','js']:
 a=read(rt+'-control-start-1');b=read(rt+'-control-end-1')
 for mode in ['registry','bookkeeping','fixedpoint']:
  for key,row in read(rt+'-'+mode+'-1').items():
   if key not in a or key not in b:continue
   lo,hi=med(a[key]),med(b[key]);base=(lo+hi)/2;value=med(row)
   alloc='—' if 'allocated' not in row else f'{med(a[key],"allocated"):,.0f} → {med(row,"allocated"):,.0f}'
   lines.append(f'| {rt} | {mode} | {key[0]} | {key[1]} | {lo/1000:.2f}–{hi/1000:.2f} | {value/1000:.2f} | {(value/base-1)*100:+.1f}% | {alloc} |')
lines+=['','| Runtime | Fields | Plain µs | Conditional µs | Ratio | Plain → conditional bytes/op |','|---|---:|---:|---:|---:|---:|']
for rt in ['native','js']:
 rows=read(rt+'-wide-1')
 op='parse' if rt=='native' else 'parseCore'
 for n in [8,32,128]:
  a=rows.get((f'wideplain{n}',op));b=rows.get((f'wideif{n}',op))
  if a and b:
   alloc='—' if 'allocated' not in a else f'{med(a,"allocated"):,.0f} → {med(b,"allocated"):,.0f}'
   lines.append(f'| {rt} | {n} | {med(a)/1000:.2f} | {med(b)/1000:.2f} | {med(b)/med(a):.2f}× | {alloc} |')
(p/'diagnostic-tables.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print('\n'.join(lines))
