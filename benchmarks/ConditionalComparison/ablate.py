import pathlib, subprocess, sys, json
root=pathlib.Path(sys.argv[1]); mode=sys.argv[2]
files=['CStructPrimitiveCodecs.cs','CompiledCompositeType.cs','CStructSelectedReader.cs','FixedPointCodec.cs']
for name in files:
 (root/'src/CStructSharp'/name).write_bytes(subprocess.check_output(['git','show','d30ee60:src/CStructSharp/'+name],cwd=root))
if mode=='registry':
 (root/'src/CStructSharp/CStructPrimitiveCodecs.cs').write_bytes(subprocess.check_output(['git','show','2c4ad4c:src/CStructSharp/CStructPrimitiveCodecs.cs'],cwd=root))
if mode=='bookkeeping':
 p=root/'src/CStructSharp/CompiledCompositeType.cs';s=p.read_text().replace('this.Fields = fields;','this.Fields = fields;\n        this.HasConditions = fields.Any(field => field.Declaration.Condition is not null);').replace('public ImmutableArray<CompiledField> Fields { get; }','public bool HasConditions { get; }\n\n    public ImmutableArray<CompiledField> Fields { get; }');p.write_text(s)
 p=root/'src/CStructSharp/CStructSelectedReader.cs';s=p.read_text().replace('var variableScope = new ConditionalVariableScope(this.compiledSizeQueries.GetCompiledComposite(strct), state.Variables);','var composite = this.compiledSizeQueries.GetCompiledComposite(strct);\n        var variableScope = composite.HasConditions ? new ConditionalVariableScope(composite, state.Variables) : null;').replace('var selection = new ConditionalFieldSelection(this.layoutExpressionEvaluator);','var selection = composite.HasConditions ? new ConditionalFieldSelection(this.layoutExpressionEvaluator) : null;').replace('bool active = selection.IsActive(field, state.Variables);','bool active = selection?.IsActive(field, state.Variables) ?? true;').replace('variableScope.CompleteField(field, state.Variables);','variableScope?.CompleteField(field, state.Variables);');p.write_text(s)
if mode=='fixedpoint':
 p=root/'src/CStructSharp/FixedPointCodec.cs';s=p.read_text().replace('name.TrimEnd(\'<\', \'>\') is "fixed16_16" or "ufixed16_16" or "fixed2_30" or "ufixed8_8"','name is '+ ' or '.join('"'+n+suffix+'"' for n in ['fixed16_16','ufixed16_16','fixed2_30','ufixed8_8'] for suffix in ['', '<', '>']));p.write_text(s)
print(subprocess.check_output(['git','diff','--stat'],cwd=root,text=True))
