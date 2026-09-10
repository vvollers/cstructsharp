namespace CStructSharp;

using System;

/// <summary>Rejects an empty, oversized, or pathologically nested layout text before the parser allocates a syntax tree.</summary>
internal static class LayoutSourceValidator
{
    public static void ValidateLayoutSource(string layout, CStructCompilationOptions options)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (string.IsNullOrWhiteSpace(layout))
        {
            throw new CStructLayoutException("Layout definition cannot be empty.");
        }

        if (options.MaxDefinitionLength <= 0 ||
            options.MaxLayoutNestingDepth <= 0 ||
            options.MaxExpressionNestingDepth <= 0 ||
            options.MaxExpressionTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Layout compilation limits must be greater than zero.");
        }

        if (layout.Length > options.MaxDefinitionLength)
        {
            throw new CStructLayoutException("Layout definition exceeds the configured length limit.");
        }

        int depth = 0;
        int expressionDepth = 0;
        bool lineComment = false;
        bool blockComment = false;
        for (int index = 0; index < layout.Length; index++)
        {
            char character = layout[index];
            char next = index + 1 < layout.Length ? layout[index + 1] : '\0';
            if (lineComment)
            {
                if (character is '\r' or '\n')
                {
                    lineComment = false;
                }

                continue;
            }

            if (blockComment)
            {
                if (character == '*' && next == '/')
                {
                    blockComment = false;
                    index++;
                }

                continue;
            }

            if (character == '/' && next == '/')
            {
                lineComment = true;
                index++;
                continue;
            }

            if (character == '/' && next == '*')
            {
                blockComment = true;
                index++;
                continue;
            }

            if (character == '{')
            {
                depth++;
                if (depth > options.MaxLayoutNestingDepth)
                {
                    throw new CStructLayoutException("Layout definition exceeds the configured nesting-depth limit.");
                }
            }
            else if (character == '}' && depth > 0)
            {
                depth--;
            }
            else if (character == '(')
            {
                expressionDepth++;
                if (expressionDepth > options.MaxExpressionNestingDepth)
                {
                    throw new CStructLayoutException(
                        "Layout definition exceeds the configured expression-nesting limit.");
                }
            }
            else if (character == ')' && expressionDepth > 0)
            {
                expressionDepth--;
            }
        }
    }
}
