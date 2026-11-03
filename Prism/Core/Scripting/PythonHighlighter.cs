using System.Collections.Generic;

namespace Prism.Core.Scripting;

public enum SyntaxKind { Text, Keyword, Builtin, String, Comment, Number, Function, Decorator }

public readonly record struct SyntaxSpan(int Start, int Length, SyntaxKind Kind);

/// <summary>Splits Python source into colored spans for the script editor. It only needs to read well while typing, so it never fails: anything it does not know is plain text.</summary>
public static class PythonHighlighter
{
    static readonly HashSet<string> Keywords =
    [
        "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif", "else", "except",
        "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise", "return", "try", "while", "with", "yield",
    ];

    static readonly HashSet<string> Builtins =
    [
        "abs", "all", "any", "bool", "dict", "enumerate", "filter", "float", "format", "getattr", "hasattr", "input", "int", "isinstance", "iter", "len", "list",
        "map", "max", "min", "next", "open", "print", "range", "repr", "reversed", "round", "self", "set", "setattr", "sorted", "str", "sum", "super", "tuple", "type", "zip",
    ];

    static bool NameStart(char c) => char.IsLetter(c) || c == '_';
    static bool NameChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>The spans cover the whole text, in order, with no gaps.</summary>
    public static List<SyntaxSpan> Tokenize(string text)
    {
        var spans = new List<SyntaxSpan>();
        void Add(int start, int end, SyntaxKind kind)
        {
            if (end <= start) return;
            if (spans.Count > 0 && spans[^1].Kind == kind) { var last = spans[^1]; spans[^1] = last with { Length = end - last.Start }; }
            else spans.Add(new SyntaxSpan(start, end - start, kind));
        }

        int i = 0, n = text.Length; bool afterDef = false;
        while (i < n)
        {
            char c = text[i]; int start = i;
            if (c == '#')
            {
                while (i < n && text[i] != '\n') i++;
                Add(start, i, SyntaxKind.Comment); continue;
            }
            if (c is '"' or '\'') { i = StringEnd(text, i); Add(start, i, SyntaxKind.String); continue; }
            if (c == '@' && LineStartsHere(text, i))
            {
                i++; while (i < n && (NameChar(text[i]) || text[i] == '.')) i++;
                Add(start, i, SyntaxKind.Decorator); continue;
            }
            if (char.IsDigit(c) || c == '.' && i + 1 < n && char.IsDigit(text[i + 1]))
            {
                while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] is '.' or '_' || text[i] is '+' or '-' && text[i - 1] is 'e' or 'E')) i++;
                Add(start, i, SyntaxKind.Number); continue;
            }
            if (NameStart(c))
            {
                while (i < n && NameChar(text[i])) i++;
                var word = text[start..i];
                if (i < n && text[i] is '"' or '\'' && word.Length <= 2 && word.ToLowerInvariant().Trim('r', 'b', 'f', 'u').Length == 0)
                {
                    i = StringEnd(text, i); Add(start, i, SyntaxKind.String); continue;   // r"...", f'...', rb"..."
                }
                int after = i; while (after < n && text[after] == ' ') after++;
                SyntaxKind kind = afterDef ? SyntaxKind.Function
                    : Keywords.Contains(word) ? SyntaxKind.Keyword
                    : Builtins.Contains(word) ? SyntaxKind.Builtin
                    : after < n && text[after] == '(' ? SyntaxKind.Function
                    : SyntaxKind.Text;
                afterDef = word is "def" or "class";
                Add(start, i, kind); continue;
            }
            i++;
            Add(start, i, SyntaxKind.Text);
        }
        return spans;
    }

    static bool LineStartsHere(string text, int at)
    {
        for (int i = at - 1; i >= 0 && text[i] != '\n'; i--) if (text[i] is not (' ' or '\t')) return false;
        return true;
    }

    /// <summary>The index just past a string starting at a quote. An unfinished single-line string stops at the end of its line.</summary>
    static int StringEnd(string text, int i)
    {
        char q = text[i]; int n = text.Length;
        if (i + 2 < n && text[i + 1] == q && text[i + 2] == q)
        {
            i += 3;
            while (i < n)
            {
                if (text[i] == '\\') { i += 2; continue; }
                if (text[i] == q && i + 2 < n && text[i + 1] == q && text[i + 2] == q) return i + 3;
                if (text[i] == q && i + 2 >= n) return n;
                i++;
            }
            return n;
        }
        i++;
        while (i < n && text[i] != '\n')
        {
            if (text[i] == '\\') { i += 2; continue; }
            if (text[i] == q) return i + 1;
            i++;
        }
        return System.Math.Min(i, n);
    }
}
