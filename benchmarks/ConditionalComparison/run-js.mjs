import fs from 'node:fs';
import { pathToFileURL } from 'node:url';
import path from 'node:path';
const [root,label,out] = process.argv.slice(2);
const bundle = path.join(root,'src/CStructSharp.Wasm/bin/Release/net10.0/browser-wasm/AppBundle');
for (const file of ['bootstrap.js','large-source.js','source-worker.js','cstructsharp-api.js']) fs.copyFileSync(path.join(root,'src/CStructSharp.Wasm',file),path.join(bundle,file));
const {dotnet} = await import(pathToFileURL(path.join(bundle,'_framework/dotnet.js')));
const runtime = await dotnet.create();
const exports = await runtime.getAssemblyExports('CStructSharpWeb.Wasm');
const managed = exports.CStructSharpWeb.Wasm.CStructExports;
const {createCStructSharpWasm} = await import(pathToFileURL(path.join(bundle,'bootstrap.js')));
const {createPublicApi} = await import(pathToFileURL(path.join(bundle,'cstructsharp-api.js')));
const api = createPublicApi(async () => createCStructSharpWasm(exports));
// The public source adapter expects the same global initialization as main.js.
globalThis.CStructSharpWasm = createCStructSharpWasm(exports);
const fixtures=JSON.parse(fs.readFileSync(process.env.BENCH_CASES || new URL('./cases.json',import.meta.url)));
const results=[];
for (const f of fixtures.filter(f=>label!=='main'||!f.conditional)) {
 const bytes=new Uint8Array(f.size).fill(f.fill);
 managed.BenchmarkCompile(f.definition);
 if(managed.BenchmarkParseCore(bytes)!==bytes.length) throw Error('Length mismatch '+f.name);
 const compiledData=JSON.parse(managed.BenchmarkParse(bytes));
 for(const op of (process.env.BENCH_OPERATIONS?.split(',') || ['compile','parseCore','parseJson','publicParse','publicDebug'])) {
  const action=op==='compile'?()=>managed.BenchmarkCompile(f.definition):op==='parseCore'?()=>managed.BenchmarkParseCore(bytes):op==='parseJson'?()=>JSON.parse(managed.BenchmarkParse(bytes)):op==='publicParse'?()=>api.parse(f.definition,bytes,{aligned:false,rootTypeName:'root'}):()=>api.parseWithDebug(f.definition,bytes,{aligned:false,rootTypeName:'root'});
  const check=await action();
  if(op.startsWith('public')) {
    if(!check.Success) throw Error(JSON.stringify(check));
    if(JSON.stringify(JSON.parse(check.Data).root)!==JSON.stringify(compiledData)) throw Error('Data mismatch '+f.name);
  }
  let sink, count=0, start=performance.now();
  while(performance.now()-start<600){sink=await action();count++;}
  const batch=Math.max(1,Math.min(1000000,Math.floor(count*200/(performance.now()-start))));
  const samples=[];
  for(let i=0;i<9;i++){
   start=performance.now();
   for(let j=0;j<batch;j++) sink=await action();
   samples.push((performance.now()-start)*1e6/batch);
  }
  results.push({scenario:f.name,operation:op,batch,ns:samples});
  console.log(`${f.name}/${op}: ${[...samples].sort((a,b)=>a-b)[4].toFixed(0)} ns`);
 }
}
fs.writeFileSync(out,JSON.stringify(results,null,2));
