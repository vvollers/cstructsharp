from pathlib import Path
import subprocess, json, hashlib
cases = [
    ('CompiledField.cs', 'this.PointerDepth == 0 && FixedPointCodec.IsType(this.CodecName)', 'this.PointerDepth != 0 && FixedPointCodec.IsType(this.CodecName)', 'IntegerInput_StillMasksFixedPointLayoutVariables'),
    ('CStruct.cs', 'normalizedGroups.Add(group, normalized);', '', 'GroupDecision_RemainsFrozenWhenNestedFieldsChangeAnExternalSelector'),
    ('CompiledSizeQueries.cs', 'selection?.IsActive(field, variables) == false', 'true', 'ConditionalCompositeSize_UsesCurrentSelector'),
    ('CStructReader.cs', 'state.Debug ? new DebugPath(debugStack, s.Name.Name) : debugStack', 'false ? new DebugPath(debugStack, s.Name.Name) : debugStack', 'ParseStreamWithDebug_RecordsCompleteUnionStorage'),
]
results=[]
for filename, original, replacement, test in cases:
    path=Path('src/CStructSharp')/filename
    saved=path.read_bytes()
    assert saved.count(original.encode()) == 1
    log=Path('artifacts/perf-implementation')/f'mutant-{test}.log'
    try:
        path.write_bytes(saved.replace(original.encode(),replacement.encode()))
        with log.open('wb') as output:
            result=subprocess.run(['dotnet','test','tests/CStructSharpTests/CStructSharpTests.csproj','-c','Release','-f','net10.0','--filter',f'FullyQualifiedName~{test}'],stdout=output,stderr=subprocess.STDOUT)
        text=log.read_text(encoding='utf-8-sig')
        assert result.returncode != 0 and f'Failed {test}' in text, text[-4000:]
        results.append(dict(file=filename,test=test,detected=True,originalSha256=hashlib.sha256(saved).hexdigest(),log=str(log)))
    finally:
        path.write_bytes(saved)
    assert path.read_bytes()==saved
Path('artifacts/perf-implementation/targeted-mutation-results.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
print('All four targeted behavioral mutants were detected; original source bytes restored.')
