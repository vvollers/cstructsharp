namespace FastImpact;

using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using BenchmarkDotNet.Running;

/// <summary>One original fixture and a typed compiled loop around its benchmark method.</summary>
internal sealed class Case
{
    private readonly object instance;
    private readonly BenchmarkCase benchmark;
    private readonly Action<long> action;

    /// <summary>Constructs an untimed adapter and rejects lifecycle features this prototype cannot preserve.</summary>
    /// <param name="benchmark">The fully expanded BenchmarkDotNet case.</param>
    /// <exception cref="NotSupportedException">The case has unsupported hooks, operation counts, arguments, or return types.</exception>
    public Case(BenchmarkCase benchmark)
    {
        this.benchmark = benchmark;

        // DisplayInfo abbreviates long fixture names; identities must retain their actual parameter values.
        string[] parameters = benchmark.Parameters.Items.Select(p => p.Name + "=" + Convert.ToString(p.Value, CultureInfo.InvariantCulture)).ToArray();
        this.Id = benchmark.Descriptor.Type.FullName + "." + benchmark.Descriptor.WorkloadMethod.Name + "|" +
                  (parameters.Length == 0 ? string.Empty : "[" + string.Join(", ", parameters) + "]");
        if (benchmark.Descriptor.IterationSetupMethod is not null || benchmark.Descriptor.IterationCleanupMethod is not null ||
            benchmark.Descriptor.OperationsPerInvoke != 1 || benchmark.Descriptor.WorkloadMethod.GetParameters().Length != 0)
        {
            throw new NotSupportedException(this.Id);
        }

        this.instance = Activator.CreateInstance(benchmark.Descriptor.Type)!;
        foreach (var parameter in benchmark.Parameters.Items)
        {
            benchmark.Descriptor.Type.GetProperty(parameter.Name)!.SetValue(this.instance, parameter.Value);
        }

        this.action = CreateLoop(this.instance, benchmark.Descriptor.WorkloadMethod);
    }

    /// <summary>Gets the unambiguous method and parameter identity.</summary>
    public string Id { get; }

    /// <summary>Gets the calibrated number of invocations per sample.</summary>
    public long Count { get; private set; }

    /// <summary>Gets the preparation cost in milliseconds.</summary>
    public double CalibrationMs { get; private set; }

    /// <summary>Projects identity for the worker manifest.</summary>
    /// <param name="item">The case.</param>
    /// <returns>Its stable identity.</returns>
    public static string Identity(Case item) => item.Id;

    /// <summary>Projects the calibrated operation count for the controller.</summary>
    /// <param name="item">The case.</param>
    /// <returns>Its calibration record.</returns>
    public static object Calibration(Case item) => new { item.Id, item.Count, item.CalibrationMs };

    /// <summary>Runs the original global setup outside measurement.</summary>
    public void Setup() => this.benchmark.Descriptor.GlobalSetupMethod?.Invoke(this.instance, null);

    /// <summary>Runs the original global cleanup outside measurement.</summary>
    public void Cleanup() => this.benchmark.Descriptor.GlobalCleanupMethod?.Invoke(this.instance, null);

    /// <summary>Invokes exactly the requested number of operations, consuming every typed result.</summary>
    /// <param name="count">The operation count.</param>
    public void Run(long count) => this.action(count);

    /// <summary>Warms and calibrates a batch; no count or timing is reused across process launches.</summary>
    /// <param name="targetMs">Desired sample duration in milliseconds.</param>
    public void Calibrate(double targetMs)
    {
        long start = Stopwatch.GetTimestamp();
        long count = 1;
        double elapsed;
        do
        {
            long tick = Stopwatch.GetTimestamp();
            this.Run(count);
            elapsed = Stopwatch.GetElapsedTime(tick).TotalMilliseconds;
            if (elapsed < 1)
            {
                count *= 2;
            }
        }
        while (elapsed < 1);

        this.Count = Math.Clamp((long)(count * targetMs / elapsed), 1, 100_000_000);
        this.Run(this.Count);
        this.CalibrationMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    /// <summary>Measures one batch including natural collections, with allocation counters outside its clock.</summary>
    /// <param name="count">The number of operations.</param>
    /// <param name="forceGc">Whether to collect before the batch; its wall cost is separately recorded.</param>
    /// <returns>Elapsed nanoseconds, allocation bytes, collections, and preparation cost.</returns>
    public object Measure(long count, bool forceGc)
    {
        long gcStart = Stopwatch.GetTimestamp();
        if (forceGc)
        {
            Program.Collect();
        }

        double gcMs = Stopwatch.GetElapsedTime(gcStart).TotalMilliseconds;
        long allocated = GC.GetTotalAllocatedBytes(true);
        long threadAllocated = GC.GetAllocatedBytesForCurrentThread();
        int gen0 = GC.CollectionCount(0);
        int gen2 = GC.CollectionCount(2);
        long start = Stopwatch.GetTimestamp();
        this.Run(count);
        double ns = Stopwatch.GetElapsedTime(start).TotalNanoseconds;
        long threadBytes = GC.GetAllocatedBytesForCurrentThread() - threadAllocated;
        long bytes = GC.GetTotalAllocatedBytes(true) - allocated;
        return new
        {
            count, ns, bytes, threadBytes, gen0 = GC.CollectionCount(0) - gen0,
            gen2 = GC.CollectionCount(2) - gen2, gcMs,
        };
    }

    /// <summary>Compiles a loop with a direct benchmark call and a typed consumer; reflection stays outside timing.</summary>
    /// <param name="instance">The prepared benchmark object.</param>
    /// <param name="method">The original operation.</param>
    /// <returns>The generated operation loop.</returns>
    /// <exception cref="NotSupportedException">The operation returns a task, non-generic value task, or stack-only result.</exception>
    private static Action<long> CreateLoop(object instance, MethodInfo method)
    {
        var count = Expression.Parameter(typeof(long), "count");
        var index = Expression.Variable(typeof(long), "i");
        var exit = Expression.Label();
        Expression call = Expression.Call(Expression.Constant(instance), method);
        Type resultType = method.ReturnType;
        if (typeof(Task).IsAssignableFrom(resultType) || resultType.IsByRefLike || resultType == typeof(ValueTask))
        {
            throw new NotSupportedException("This prototype supports synchronous results and ValueTask<T> only.");
        }

        if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            call = Expression.Call(Expression.Call(call, "GetAwaiter", Type.EmptyTypes), "GetResult", Type.EmptyTypes);
            resultType = resultType.GetGenericArguments()[0];
        }

        if (resultType != typeof(void))
        {
            call = Expression.Call(typeof(Sink<>).MakeGenericType(resultType).GetMethod("Consume")!, call);
        }

        Expression body = Expression.Block(call, Expression.PostIncrementAssign(index), Expression.Empty());
        Expression conditional = Expression.IfThenElse(Expression.LessThan(index, count), body, Expression.Break(exit));
        Expression loop = Expression.Block([index], Expression.Assign(index, Expression.Constant(0L)), Expression.Loop(conditional, exit));
        return Expression.Lambda<Action<long>>(loop, count).Compile();
    }
}
