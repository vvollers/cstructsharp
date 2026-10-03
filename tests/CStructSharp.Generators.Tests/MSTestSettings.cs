// Each test owns its Roslyn compilation and loaded consumer assembly. Bound concurrent compilation memory;
// development tooling can override the worker count. Snapshot recording still writes distinct files per test.
[assembly: Parallelize(Workers = 4, Scope = ExecutionScope.MethodLevel)]
