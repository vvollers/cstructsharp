namespace CStructSharp.Build;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

/// <summary>
///     Writes a copy of a compiler XML documentation file that keeps only the entries a consumer can see: externally
///     visible types and their public or protected members.
/// </summary>
/// <remarks>
///     The C# compiler documents every member that has a documentation comment, private and internal ones included,
///     and this repository documents every declaration. Consumers' IntelliSense only ever shows the public surface, so
///     the rest only enlarges the package. Visibility comes from the compiled assembly's metadata. Members are matched
///     by name: every overload of a name that has at least one visible overload is kept. An entry the tool cannot
///     resolve to a type is kept, so a mismatch can only leave extra text, never drop public documentation.
/// </remarks>
internal static class Program
{
    /// <summary>How a documentation entry is treated.</summary>
    private enum Visibility
    {
        /// <summary>A consumer can see the documented item; the entry stays.</summary>
        Visible,

        /// <summary>The documented item is internal or private; the entry is removed.</summary>
        Hidden,

        /// <summary>The entry names no type of the assembly; it stays so nothing public can be lost.</summary>
        Unresolved,
    }

    /// <summary>Filters the documentation file.</summary>
    /// <param name="args">The assembly path, the input XML path, and the output XML path.</param>
    /// <returns>0 on success, 2 for wrong arguments.</returns>
    public static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: PublicDocumentation <assembly.dll> <input.xml> <output.xml>");
            return 2;
        }

        Dictionary<string, VisibleType> types = ReadVisibility(args[0]);
        var document = XDocument.Load(args[1], LoadOptions.PreserveWhitespace);
        XElement members = document.Root?.Element("members") ?? throw new InvalidDataException($"'{args[1]}' has no <members> element.");

        int kept = 0;
        int removed = 0;
        int unresolved = 0;
        foreach (XElement member in members.Elements("member").ToList())
        {
            switch (Classify(member.Attribute("name")?.Value ?? string.Empty, types))
            {
            case Visibility.Visible:
                kept++;
                break;
            case Visibility.Unresolved:
                unresolved++;
                break;
            default:
                // The whitespace before the element belongs to it; removing both keeps the file's layout.
                if (member.PreviousNode is XText indentation && string.IsNullOrWhiteSpace(indentation.Value))
                {
                    indentation.Remove();
                }

                member.Remove();
                removed++;
                break;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
        document.Save(args[2], SaveOptions.DisableFormatting);
        Console.WriteLine($"PublicDocumentation: kept {kept} public entries, removed {removed} non-public, kept {unresolved} unresolved -> {args[2]}");
        return 0;
    }

    /// <summary>Decides whether one documentation entry describes something a consumer can see.</summary>
    /// <param name="id">The entry's documentation ID, such as <c>M:CStructSharp.CStruct.Parse(System.Byte[])</c>.</param>
    /// <param name="types">Every type of the assembly by its documentation name.</param>
    /// <returns>The entry's visibility.</returns>
    private static Visibility Classify(string id, Dictionary<string, VisibleType> types)
    {
        if (id.Length < 3 || id[1] != ':')
        {
            return Visibility.Unresolved;
        }

        // Parameters and a conversion operator's return type follow the name; neither takes part in name matching.
        string body = id[2..];
        int cut = body.IndexOfAny(['(', '~']);
        if (cut >= 0)
        {
            body = body[..cut];
        }

        if (id[0] == 'T')
        {
            return types.TryGetValue(body, out VisibleType? type) ? (type.IsVisible ? Visibility.Visible : Visibility.Hidden) : Visibility.Unresolved;
        }

        int dot = body.LastIndexOf('.');
        if (dot < 0 || !types.TryGetValue(body[..dot], out VisibleType? owner))
        {
            return Visibility.Unresolved;
        }

        if (!owner.IsVisible)
        {
            return Visibility.Hidden;
        }

        // A generic method carries its arity (``1); constructors are #ctor; an explicit interface implementation
        // spells the dots of its metadata name (System.IDisposable.Dispose) as '#'.
        string name = body[(dot + 1)..];
        int arity = name.IndexOf("``", StringComparison.Ordinal);
        if (arity >= 0)
        {
            name = name[..arity];
        }

        name = name switch
        {
            "#ctor" => ".ctor",
            "#cctor" => ".cctor",
            _ => name.Replace('#', '.'),
        };
        return owner.VisibleMembers.Contains(name) ? Visibility.Visible : Visibility.Hidden;
    }

    /// <summary>Reads every type's documentation name, its visibility, and the names of its visible members.</summary>
    /// <param name="assemblyPath">The compiled assembly.</param>
    /// <returns>The types by documentation name (namespace and declaring types joined with dots).</returns>
    private static Dictionary<string, VisibleType> ReadVisibility(string assemblyPath)
    {
        using FileStream stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        var names = new Dictionary<TypeDefinitionHandle, string>();
        var visible = new Dictionary<TypeDefinitionHandle, bool>();
        var result = new Dictionary<string, VisibleType>(StringComparer.Ordinal);

        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            string name = NameOf(handle);
            var type = new VisibleType(IsVisible(handle));
            TypeDefinition definition = reader.GetTypeDefinition(handle);
            foreach (MethodDefinitionHandle method in definition.GetMethods())
            {
                MethodDefinition value = reader.GetMethodDefinition(method);
                if (IsVisibleMember(value.Attributes))
                {
                    type.VisibleMembers.Add(reader.GetString(value.Name));
                }
            }

            foreach (FieldDefinitionHandle field in definition.GetFields())
            {
                FieldDefinition value = reader.GetFieldDefinition(field);
                if (IsVisibleMember((MethodAttributes)(int)(value.Attributes & FieldAttributes.FieldAccessMask)))
                {
                    type.VisibleMembers.Add(reader.GetString(value.Name));
                }
            }

            foreach (PropertyDefinitionHandle property in definition.GetProperties())
            {
                PropertyDefinition value = reader.GetPropertyDefinition(property);
                PropertyAccessors accessors = value.GetAccessors();
                if (IsVisibleAccessor(accessors.Getter) || IsVisibleAccessor(accessors.Setter))
                {
                    type.VisibleMembers.Add(reader.GetString(value.Name));
                }
            }

            foreach (EventDefinitionHandle @event in definition.GetEvents())
            {
                EventDefinition value = reader.GetEventDefinition(@event);
                if (IsVisibleAccessor(value.GetAccessors().Adder))
                {
                    type.VisibleMembers.Add(reader.GetString(value.Name));
                }
            }

            result[name] = type;
        }

        return result;

        // The documentation name: namespace-qualified for a top-level type, the declaring type's name and a dot for
        // a nested one (documentation IDs write nesting with '.', not '+').
        string NameOf(TypeDefinitionHandle handle)
        {
            if (names.TryGetValue(handle, out string? known))
            {
                return known;
            }

            TypeDefinition definition = reader.GetTypeDefinition(handle);
            string simple = reader.GetString(definition.Name);
            TypeDefinitionHandle declaring = definition.GetDeclaringType();
            string name = !declaring.IsNil ? NameOf(declaring) + "." + simple
                : definition.Namespace.IsNil ? simple : reader.GetString(definition.Namespace) + "." + simple;
            names[handle] = name;
            return name;
        }

        // Visible outside the assembly: public at the top level, or public/protected inside a visible declaring type.
        bool IsVisible(TypeDefinitionHandle handle)
        {
            if (visible.TryGetValue(handle, out bool known))
            {
                return known;
            }

            TypeDefinition definition = reader.GetTypeDefinition(handle);
            TypeAttributes access = definition.Attributes & TypeAttributes.VisibilityMask;
            bool result = access switch
            {
                TypeAttributes.Public => true,
                TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem => IsVisible(definition.GetDeclaringType()),
                _ => false,
            };
            visible[handle] = result;
            return result;
        }

        // An accessor makes its property or event visible when the accessor itself is visible.
        bool IsVisibleAccessor(MethodDefinitionHandle accessor)
            => !accessor.IsNil && IsVisibleMember(reader.GetMethodDefinition(accessor).Attributes);
    }

    /// <summary>Whether a member access level is visible to a consumer: public, protected, or protected internal.</summary>
    /// <param name="attributes">The member's attributes; field access values coincide with method access values.</param>
    /// <returns>True when a consumer can see the member.</returns>
    private static bool IsVisibleMember(MethodAttributes attributes)
        => (attributes & MethodAttributes.MemberAccessMask) is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;

    /// <summary>A type's visibility and the names of its members a consumer can see.</summary>
    /// <param name="IsVisible">Whether the type itself is visible outside the assembly.</param>
    private sealed record VisibleType(bool IsVisible)
    {
        /// <summary>Gets the metadata names of the visible methods, fields, properties, and events.</summary>
        public HashSet<string> VisibleMembers { get; } = new(StringComparer.Ordinal);
    }
}
