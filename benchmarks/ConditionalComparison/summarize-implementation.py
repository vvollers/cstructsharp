import argparse, json, statistics as st
from pathlib import Path
root=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser()
parser.add_argument('--results', type=Path, default=root/'agentdocs/performance-improvements/results')
parser.add_argument('--output', type=Path, default=root/'agentdocs/performance-improvements')
args=parser.parse_args()
results=args.results
out=args.output
out.mkdir(parents=True,exist_ok=True)
summary={}
lines=['# Fresh Release comparison tables','', 'Times are microseconds. Values are the median of two process medians; each process records nine batches. Ratios compare optimized with the named fixed baseline. Original historical measurements remain under `agentdocs/benchmark-results`.', '']
for runtime in ['native','js']:
 data={label:[{(row['scenario'],row['operation']):row for row in json.loads((results/f'{runtime}-{label}-{r}.json').read_text(encoding='utf-8-sig'))} for r in [1,2]] for label in ['main','feature','optimized']}
 keys=list(data['optimized'][0])
 lines+=['## '+runtime,'','| Scenario / operation | Main | Feature | Optimized | Opt/main | Opt/feature | Opt/main by launch |','|---|---:|---:|---:|---:|---:|---|']
 summary[runtime]={}
 for key in keys:
  vals={label:[st.median(run[key]['ns']) for run in runs] for label,runs in data.items() if key in runs[0]}
  med={label:st.median(v) for label,v in vals.items()}
  ratio=lambda label:med['optimized']/med[label] if label in med else None
  launch=[a/b for a,b in zip(vals['optimized'],vals['main'])] if 'main' in vals else []
  row={'ns':med,'launchNs':vals,'mainRatio':ratio('main'),'featureRatio':ratio('feature'),'mainLaunchRatios':launch}
  for label,runs in data.items():
   if key in runs[0] and 'allocated' in runs[0][key]: row.setdefault('allocated',{})[label]=st.median(st.median(run[key]['allocated']) for run in runs)
  summary[runtime]['/'.join(key)]=row
  def fmt(v):return '—' if v is None else f'{v:.2f}'
  lines.append('| '+' / '.join(key)+' | '+' | '.join(fmt(med.get(label)/1000 if label in med else None) for label in ['main','feature','optimized'])+' | '+fmt(ratio('main'))+'× | '+fmt(ratio('feature'))+'× | '+', '.join(f'{v:.2f}×' for v in launch)+' |')
 lines+=['','### Conditional price','','| Pair / operation | Feature conditional/plain | Optimized conditional/plain |','|---|---:|---:|']
 pairs=[('plain1','if1'),('plain1','switch1'),('plain128','if128'),('plain128','switch128')]+[(f'wideplain{n}',f'wideif{n}') for n in [8,32,128]]+[('mixedplain128','mixedswitch128'),('mixedplain128','nestedif128')]
 for plain,cond in pairs:
  for op in (['compile','parse','debug'] if runtime=='native' else ['compile','parseCore','parseJson','compiledParse','compiledDebug']):
   a=summary[runtime].get(f'{plain}/{op}');b=summary[runtime].get(f'{cond}/{op}')
   if a and b:
    ratios=[f"{b['ns'][label]/a['ns'][label]:.2f}×" if label in a['ns'] and label in b['ns'] else '—' for label in ['feature','optimized']]
    lines.append(f'| {cond}/{plain} / {op} | '+ ' | '.join(ratios)+' |')
 lines+=['']
lines+=['## Native allocated bytes per operation','','| Scenario / operation | Main | Feature | Optimized |','|---|---:|---:|---:|']
for key,row in summary['native'].items():
 lines.append('| '+key+' | '+' | '.join(f"{row['allocated'][label]:.0f}" if label in row['allocated'] else '—' for label in ['main','feature','optimized'])+' |')
(out/'tables.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
(out/'summary.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
print('Repeated >10% slowdowns against main:')
for runtime,rows in summary.items():
 for key,row in rows.items():
  if len(row['mainLaunchRatios'])==2 and min(row['mainLaunchRatios'])>1.1:print(runtime,key,row['mainLaunchRatios'])
