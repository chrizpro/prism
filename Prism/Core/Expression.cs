using System;
using System.Collections.Generic;
using System.Globalization;

namespace Prism.Core;

/// <summary>
/// A small formula parser for curves defined by equations: numbers, the variable t, pi/e/tau, + - * / ^, brackets and the usual functions.
/// Implied multiplication works (2t, 3(t+1), 2 sin(t)). Trigonometry takes and returns degrees unless told otherwise.
/// </summary>
public sealed class Expression
{
    abstract record Node;
    sealed record Num(double V) : Node;
    sealed record Var(string Name) : Node;
    sealed record Neg(Node A) : Node;
    sealed record Bin(char Op, Node A, Node B) : Node;
    sealed record Call(string Fn, List<Node> Args) : Node;

    readonly Node _root;
    public string Text { get; }
    Expression(Node root, string text) { _root = root; Text = text; }

    public static Expression Parse(string text)
    {
        var p = new Parser(text);
        var n = p.ParseAll();
        return new Expression(n, text);
    }

    /// <summary>Value for the given t. Degrees: trig functions work in degrees.</summary>
    public double Eval(double t, bool degrees = true) => Eval(_root, t, degrees, null);

    public double Eval(Func<string, double> resolve, bool degrees = true) => Eval(_root, 0, degrees, resolve);

    static double Eval(Node n, double t, bool deg, Func<string, double>? resolve)
    {
        switch (n)
        {
            case Num v: return v.V;
            case Var v:
                return v.Name switch
                {
                    "t" => resolve == null ? t : resolve("t"), "pi" => Math.PI, "e" => Math.E, "tau" => 2 * Math.PI,
                    _ => resolve != null ? resolve(v.Name) : throw new FormatException($"Unknown name '{v.Name}'. Use t for the parameter."),
                };
            case Neg a: return -Eval(a.A, t, deg, resolve);
            case Bin b:
            {
                double x = Eval(b.A, t, deg, resolve), y = Eval(b.B, t, deg, resolve);
                return b.Op switch { '+' => x + y, '-' => x - y, '*' => x * y, '/' => x / y, _ => Math.Pow(x, y) };
            }
            case Call c:
            {
                var a = c.Args.ConvertAll(x => Eval(x, t, deg, resolve));
                double R(double x) => deg ? x * Math.PI / 180 : x;
                double D(double x) => deg ? x * 180 / Math.PI : x;
                void Need(int k) { if (a.Count != k) throw new FormatException($"{c.Fn}() takes {k} value{(k == 1 ? "" : "s")}."); }
                switch (c.Fn)
                {
                    case "sin": Need(1); return Math.Sin(R(a[0]));
                    case "cos": Need(1); return Math.Cos(R(a[0]));
                    case "tan": Need(1); return Math.Tan(R(a[0]));
                    case "asin": Need(1); return D(Math.Asin(a[0]));
                    case "acos": Need(1); return D(Math.Acos(a[0]));
                    case "atan": Need(1); return D(Math.Atan(a[0]));
                    case "atan2": Need(2); return D(Math.Atan2(a[0], a[1]));
                    case "sinh": Need(1); return Math.Sinh(a[0]);
                    case "cosh": Need(1); return Math.Cosh(a[0]);
                    case "tanh": Need(1); return Math.Tanh(a[0]);
                    case "sqrt": Need(1); return Math.Sqrt(a[0]);
                    case "abs": Need(1); return Math.Abs(a[0]);
                    case "exp": Need(1); return Math.Exp(a[0]);
                    case "ln": Need(1); return Math.Log(a[0]);
                    case "log": Need(1); return Math.Log10(a[0]);
                    case "floor": Need(1); return Math.Floor(a[0]);
                    case "ceil": Need(1); return Math.Ceiling(a[0]);
                    case "round": Need(1); return Math.Round(a[0]);
                    case "sign": Need(1); return Math.Sign(a[0]);
                    case "pow": Need(2); return Math.Pow(a[0], a[1]);
                    case "mod": Need(2); return a[0] - a[1] * Math.Floor(a[0] / a[1]);
                    case "hypot": Need(2); return Math.Sqrt(a[0] * a[0] + a[1] * a[1]);
                    case "min": Need(2); return Math.Min(a[0], a[1]);
                    case "max": Need(2); return Math.Max(a[0], a[1]);
                    default: throw new FormatException($"Unknown function '{c.Fn}'.");
                }
            }
            default: throw new FormatException("Cannot read that formula.");
        }
    }

    sealed class Parser(string s)
    {
        int _i;
        char Peek() { while (_i < s.Length && char.IsWhiteSpace(s[_i])) _i++; return _i < s.Length ? s[_i] : '\0'; }

        public Node ParseAll()
        {
            if (string.IsNullOrWhiteSpace(s)) throw new FormatException("The formula is empty.");
            var n = ParseSum();
            if (Peek() != '\0') throw new FormatException($"Unexpected '{Peek()}' in \"{s}\".");
            return n;
        }

        Node ParseSum()
        {
            var a = ParseProduct();
            while (Peek() is '+' or '-') { char op = s[_i++]; a = new Bin(op, a, ParseProduct()); }
            return a;
        }

        Node ParseProduct()
        {
            var a = ParseUnary();
            while (true)
            {
                char c = Peek();
                if (c is '*' or '/') { _i++; a = new Bin(c, a, ParseUnary()); }
                else if (char.IsLetter(c) || c == '(' ) a = new Bin('*', a, ParsePower());        // implied multiplication: 2t, 3(t+1), 2 sin(t)
                else return a;
            }
        }

        Node ParseUnary()
        {
            char c = Peek();
            if (c == '-') { _i++; return new Neg(ParseUnary()); }
            if (c == '+') { _i++; return ParseUnary(); }
            return ParsePower();
        }

        Node ParsePower()
        {
            var a = ParseAtom();
            if (Peek() == '^') { _i++; return new Bin('^', a, ParseUnary()); }       // right-associative, and -2^2 style exponents work
            return a;
        }

        Node ParseAtom()
        {
            char c = Peek();
            if (c == '(') { _i++; var n = ParseSum(); Expect(')'); return n; }
            if (char.IsDigit(c) || c == '.')
            {
                int st = _i;
                while (_i < s.Length && (char.IsDigit(s[_i]) || s[_i] == '.')) _i++;
                if (_i < s.Length && (s[_i] is 'e' or 'E') && _i + 1 < s.Length && (char.IsDigit(s[_i + 1]) || (s[_i + 1] is '+' or '-' && _i + 2 < s.Length && char.IsDigit(s[_i + 2]))))
                {
                    _i += 2; while (_i < s.Length && char.IsDigit(s[_i])) _i++;
                }
                if (!double.TryParse(s.AsSpan(st, _i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) throw new FormatException($"'{s[st.._i]}' is not a number.");
                return new Num(v);
            }
            if (char.IsLetter(c))
            {
                int st = _i;
                // a dot followed by a letter continues the name, so a dimension can be reached as sketch1.width or doc.wall
                while (_i < s.Length && (char.IsLetterOrDigit(s[_i]) || s[_i] == '_' || (s[_i] == '.' && _i + 1 < s.Length && char.IsLetter(s[_i + 1])))) _i++;
                string name = s[st.._i].ToLowerInvariant();
                if (Peek() == '(')
                {
                    _i++; var args = new List<Node>();
                    if (Peek() != ')') { args.Add(ParseSum()); while (Peek() == ',') { _i++; args.Add(ParseSum()); } }
                    Expect(')');
                    return new Call(name, args);
                }
                return new Var(name);
            }
            throw new FormatException(c == '\0' ? $"The formula \"{s}\" ends too early." : $"Unexpected '{c}' in \"{s}\".");
        }

        void Expect(char c) { if (Peek() != c) throw new FormatException($"Missing '{c}' in \"{s}\"."); _i++; }
    }
}
