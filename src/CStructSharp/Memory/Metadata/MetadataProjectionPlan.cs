namespace CStructSharp.Memory.Metadata;

using CStructSharp.Diagnostics;

/// <summary>Resolves requested paths against shallow metadata before either importer compiles selected definitions.</summary>
internal sealed class MetadataProjectionPlan
{
    private readonly Func<string, Action, Node> describe;
    private readonly Dictionary<string, Node> nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> members = new(StringComparer.Ordinal);
    private readonly HashSet<string> required = new(StringComparer.Ordinal);
    private readonly HashSet<string> expanded = new(StringComparer.Ordinal);
    private readonly int maxTypes;
    private readonly int maxWork;
    private readonly Dictionary<(string Type, string Member), PromotionMatches> promotions = [];
    private readonly CancellationToken cancellationToken;
    private readonly Action charge;
    private int work;

    /// <summary>Snapshots paths and computes their by-value closure without following unrequested pointers.</summary>
    /// <param name="root">Metadata identity of the root.</param>
    /// <param name="paths">Relative session paths; an empty path selects the whole root by value.</param>
    /// <param name="describe">Reads one shallow type without compiling dependencies.</param>
    /// <param name="maxTypes">Maximum shallow descriptors.</param>
    /// <param name="maxWork">Maximum path entries, steps, native fields, promotion visits and closure work.</param>
    /// <param name="cancellationToken">Observed during planning, promotion search and closure expansion.</param>
    internal MetadataProjectionPlan(string root, IEnumerable<string> paths, Func<string, Action, Node> describe, int maxTypes, int maxWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        this.describe = describe;
        this.maxTypes = maxTypes;
        this.maxWork = maxWork;
        this.cancellationToken = cancellationToken;
        this.charge = this.Charge;
        var selected = new List<string>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            this.Charge();
            ArgumentNullException.ThrowIfNull(path);

            if (unique.Add(path))
            {
                selected.Add(path);
                this.Select(root, path);
            }
        }

        if (selected.Count == 0)
        {
            throw new ArgumentException("A projection needs at least one selected path.", nameof(paths));
        }

        this.Paths = selected.AsReadOnly();
    }

    /// <summary>Gets the distinct requested paths in caller order.</summary>
    internal IReadOnlyList<string> Paths { get; }

    /// <summary>Checks whether a pointer target is required by any requested path or by-value closure.</summary>
    /// <param name="id">Native metadata identity.</param>
    /// <returns>Whether the importer should materialize the target.</returns>
    internal bool Requires(string id) => this.required.Contains(id);

    /// <summary>Checks whether an immediate composite member belongs to the union of requested paths.</summary>
    /// <param name="id">Composite metadata identity.</param>
    /// <param name="name">Immediate member name.</param>
    /// <returns>Whether its descriptor and placement should be imported.</returns>
    internal bool Includes(string id, string name) => this.members.TryGetValue(id, out HashSet<string>? selected) && selected.Contains(name);

    /// <summary>Retains the omitted immediate names without claiming that their descriptors were validated.</summary>
    /// <param name="id">Composite metadata identity.</param>
    /// <returns>An owned read-only list in metadata order.</returns>
    internal IReadOnlyList<string> Unselected(string id)
    {
        var result = new List<string>();
        foreach (Member member in this.Get(id).Members)
        {
            this.Charge();
            if (!this.Includes(id, member.Name))
            {
                result.Add(member.Name);
            }
        }

        return result.AsReadOnly();
    }

    /// <summary>Loads a bounded shallow descriptor and observes cancellation even on a cache hit.</summary>
    /// <param name="id">Metadata identity.</param>
    /// <returns>The uncompiled type shape.</returns>
    private Node Get(string id)
    {
        this.cancellationToken.ThrowIfCancellationRequested();
        if (!this.nodes.TryGetValue(id, out Node? node))
        {
            if (this.nodes.Count >= this.maxTypes)
            {
                throw new CStructLayoutException("Projection exceeds its descriptor budget.");
            }

            this.Charge();
            node = this.describe(id, this.charge);
            this.nodes.Add(id, node);
            if (node.Dependencies is { } dependencies)
            {
                foreach (string dependency in dependencies)
                {
                    this.required.Add(dependency);
                    _ = this.Get(dependency);
                }
            }
        }

        return node;
    }

    /// <summary>Resolves one ordinary member/index/pointer path and selects its terminal by-value representation.</summary>
    /// <param name="root">Initial type.</param>
    /// <param name="path">Relative path.</param>
    private void Select(string root, string path)
    {
        this.ValidatePath(path);
        MemorySession.PathStep[] steps = MemorySession.Steps(path);
        if (steps.Length > MemorySchema.MaxDefinitionNestingDepth)
        {
            throw new CStructPathException("Projection path exceeds its nesting limit.");
        }

        string current = root;
        for (int index = 0; index < steps.Length; index++)
        {
            this.Charge();
            this.required.Add(current);
            Node node = this.Get(current);
            MemorySession.PathStep step = steps[index];
            if (node.Kind == MemoryTypeKind.Pointer && step.Member == "address" && index == steps.Length - 1)
            {
                return;
            }

            if (node.Kind == MemoryTypeKind.Pointer && step.Member == "value" && node.Element is { } target)
            {
                current = target;
                continue;
            }

            if (node.Kind == MemoryTypeKind.Array && step.Member is null && step.Index < node.Count)
            {
                current = node.Element!;
                continue;
            }

            if (node.Kind is MemoryTypeKind.Struct or MemoryTypeKind.Union && step.Member is { } name)
            {
                PromotionMatches found = this.Find(current, name, new HashSet<string>(StringComparer.Ordinal), 0);
                var matches = found.Chains;
                if (matches.Length != 1)
                {
                    throw new CStructPathException(matches.Length == 0 ? $"Member '{current}.{name}' is absent." : $"Ambiguous promoted member '{name}'.");
                }

                foreach ((string owner, Member field) in matches[0])
                {
                    this.required.Add(owner);
                    this.Include(owner, field.Name);
                    current = this.Get(owner).MemberType!(field.Name);
                }

                continue;
            }

            throw new CStructPathException($"Cannot traverse '{step}' through '{current}' in a projection.");
        }

        this.Expand(current);
    }

    /// <summary>Bounds path text and member/index counts before the ordinary parser can allocate segments or cache tokens.</summary>
    /// <param name="path">Caller-supplied relative path.</param>
    /// <exception cref="CStructPathException">More than 256 member/index steps are present.</exception>
    private void ValidatePath(string path)
    {
        int steps = 0;
        bool started = false;
        foreach (char character in path)
        {
            this.Charge();
            if (!started)
            {
                if (char.IsWhiteSpace(character))
                {
                    continue;
                }

                started = true;
                steps = character == '[' ? 0 : 1;
            }

            if (character is '.' or '[')
            {
                steps++;
                if (steps > MemorySchema.MaxDefinitionNestingDepth)
                {
                    throw new CStructPathException("Projection path exceeds its nesting limit.");
                }
            }
        }
    }

    /// <summary>Memoizes promotion searches by type/name and retains at most two candidates, enough to prove ambiguity.</summary>
    /// <param name="id">Composite searched.</param>
    /// <param name="name">Requested member.</param>
    /// <param name="active">Current promotion ancestry.</param>
    /// <param name="depth">Bounded promotion depth.</param>
    /// <returns>Zero, one or two candidate chains and the greatest relative search depth.</returns>
    private PromotionMatches Find(string id, string name, HashSet<string> active, int depth)
    {
        this.Charge();
        if (depth > MemorySchema.MaxDefinitionNestingDepth || active.Contains(id))
        {
            throw new CStructLayoutException("Projection promotion graph is recursive or too deep.");
        }

        if (this.promotions.TryGetValue((id, name), out PromotionMatches? cached))
        {
            if (depth + cached.Depth > MemorySchema.MaxDefinitionNestingDepth)
            {
                throw new CStructLayoutException("Projection promotion graph exceeds its nesting limit.");
            }

            return cached;
        }

        Node node = this.Get(id);
        if (node.Kind is not (MemoryTypeKind.Struct or MemoryTypeKind.Union))
        {
            throw new CStructLayoutException("Only composite members can be anonymously promoted in a projection.");
        }

        active.Add(id);
        var matches = new List<(string Owner, Member Field)[]>();
        int maximum = 0;
        foreach (Member field in node.Members)
        {
            this.Charge();
            if (field.Name == name)
            {
                matches.Add([(id, field)]);
            }
            else if (field.Promoted)
            {
                PromotionMatches nested = this.Find(node.MemberType!(field.Name), name, active, depth + 1);
                maximum = Math.Max(maximum, nested.Depth + 1);
                foreach (var chain in nested.Chains)
                {
                    matches.Add([(id, field), .. chain]);
                    if (matches.Count == 2)
                    {
                        break;
                    }
                }
            }

            if (matches.Count == 2)
            {
                break;
            }
        }

        active.Remove(id);
        var result = new PromotionMatches(matches.ToArray(), maximum);
        this.promotions.Add((id, name), result);
        return result;
    }

    /// <summary>Charges bounded planning work and checks cancellation before the next unit.</summary>
    /// <exception cref="CStructLayoutException">The explicit projection work budget is exhausted.</exception>
    private void Charge()
    {
        this.cancellationToken.ThrowIfCancellationRequested();
        if (this.work >= this.maxWork)
        {
            throw new CStructLayoutException("Projection exceeds its planning work budget.");
        }

        this.work++;
    }

    /// <summary>Selects a complete terminal value, following arrays and embedded composites but stopping at pointer storage.</summary>
    /// <param name="id">Terminal type.</param>
    private void Expand(string id)
    {
        var pending = new Stack<string>();
        pending.Push(id);
        while (pending.Count > 0)
        {
            this.Charge();
            string current = pending.Pop();
            if (!this.expanded.Add(current))
            {
                continue;
            }

            this.required.Add(current);
            Node node = this.Get(current);
            if (node.Kind == MemoryTypeKind.Incomplete)
            {
                throw new CStructPathException($"Selected type '{current}' has no value representation.");
            }

            if (node.Kind == MemoryTypeKind.Array)
            {
                pending.Push(node.Element!);
            }
            else if (node.Kind is MemoryTypeKind.Struct or MemoryTypeKind.Union)
            {
                foreach (Member member in node.Members)
                {
                    this.Charge();
                    this.Include(current, member.Name);
                    pending.Push(node.MemberType!(member.Name));
                }
            }
        }
    }

    /// <summary>Adds one immediate member to the immutable import's planned closure.</summary>
    /// <param name="id">Composite identity.</param>
    /// <param name="name">Member name.</param>
    private void Include(string id, string name)
    {
        if (!this.members.TryGetValue(id, out HashSet<string>? names))
        {
            names = new(StringComparer.Ordinal);
            this.members.Add(id, names);
        }

        names.Add(name);
    }

    /// <summary>Bounded promotion candidates and depth, reusable across a shared metadata DAG.</summary>
    /// <param name="Chains">At most two candidate chains.</param>
    /// <param name="Depth">Greatest relative promotion depth visited.</param>
    private sealed record PromotionMatches((string Owner, Member Field)[][] Chains, int Depth);

    /// <summary>A shallow type; member types are resolved lazily so unrelated malformed descriptors are not traversed.</summary>
    /// <param name="Kind">Storage category.</param>
    /// <param name="Members">Immediate names and promotion markers.</param>
    /// <param name="MemberType">Resolves one requested member's native identity.</param>
    /// <param name="Element">Array element or pointer target.</param>
    /// <param name="Count">Declared array count.</param>
    /// <param name="Dependencies">Scalar representations needed for selected bit or pointer storage.</param>
    internal sealed record Node(MemoryTypeKind Kind, IReadOnlyList<Member> Members, Func<string, string>? MemberType = null, string? Element = null, int Count = 0, IReadOnlyList<string>? Dependencies = null);

    /// <summary>One immediate member whose descriptor is not evaluated until selected or searched for promotion.</summary>
    /// <param name="Name">Metadata name, including generated names for anonymous members.</param>
    /// <param name="Promoted">Whether its members share the containing namespace.</param>
    internal sealed record Member(string Name, bool Promoted);
}
