namespace CStructSharp;

using System;
using System.Linq.Expressions;
using System.Reflection;

/// <summary>
///     Expression-tree member getters for <see cref="PocoDataBinding"/>; kept apart so that a build without dynamic
///     code support (the trimmed browser bundle) does not link <c>System.Linq.Expressions</c> at all.
/// </summary>
internal static class PocoCompiledAccessors
{
    public static Func<object, object?> BuildPropertyGetter(PropertyInfo property)
    {
        ParameterExpression target = Expression.Parameter(typeof(object), "target");
        UnaryExpression body = Expression.Convert(
            Expression.Property(Expression.Convert(target, property.DeclaringType!), property),
            typeof(object));
        return Expression.Lambda<Func<object, object?>>(body, target).Compile();
    }

    public static Func<object, object?> BuildFieldGetter(FieldInfo field)
    {
        ParameterExpression target = Expression.Parameter(typeof(object), "target");
        UnaryExpression body = Expression.Convert(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
            typeof(object));
        return Expression.Lambda<Func<object, object?>>(body, target).Compile();
    }
}
