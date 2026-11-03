using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Prism.Core.Data;

/// <summary>A table of cells, like one sheet of a spreadsheet. Cells hold text; one that starts with = is a formula.</summary>
public sealed class DataSheet
{
    public string Name { get; set; } = "Sheet 1";
    /// <summary>Row by row. Rows may be shorter than the widest one.</summary>
    public List<List<string>> Rows { get; set; } = [];

    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public int RowCount => Rows.Count;
    public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(r => r.Count);
    public bool IsEmpty => Rows.All(r => r.All(string.IsNullOrEmpty));

    public string Raw(int row, int column) => row >= 0 && row < Rows.Count && column >= 0 && column < Rows[row].Count ? Rows[row][column] : "";

    public void Set(int row, int column, string value)
    {
        if (row < 0 || column < 0) return;
        while (Rows.Count <= row) Rows.Add([]);
        var r = Rows[row];
        while (r.Count <= column) r.Add("");
        r[column] = value;
    }

    public void InsertRow(int at) { at = Math.Clamp(at, 0, Rows.Count); Rows.Insert(at, []); }
    public void DeleteRow(int at) { if (at >= 0 && at < Rows.Count) Rows.RemoveAt(at); }
    public void InsertColumn(int at) { foreach (var r in Rows) if (at < r.Count) r.Insert(at, ""); }
    public void DeleteColumn(int at) { foreach (var r in Rows) if (at >= 0 && at < r.Count) r.RemoveAt(at); }

    /// <summary>The first, second... column as the letters a spreadsheet uses: A, B, ... Z, AA, AB.</summary>
    public static string ColumnName(int column)
    {
        var s = "";
        for (int c = column + 1; c > 0; c = (c - 1) / 26) s = (char)('A' + (c - 1) % 26) + s;
        return s;
    }

    /// <summary>A column's index from its letters; -1 when they are not letters.</summary>
    public static int ColumnIndex(string letters)
    {
        int c = 0;
        foreach (char ch in letters.ToUpperInvariant()) { if (ch is < 'A' or > 'Z') return -1; c = c * 26 + (ch - 'A' + 1); }
        return c - 1;
    }

    // ------------------------------------------------------------------ values
    /// <summary>What a cell shows: its text, or for a formula the result (or an error such as #DIV/0!).</summary>
    public string Display(int row, int column, bool full = false)
    {
        string raw = Raw(row, column);
        if (!raw.StartsWith('=')) return raw;
        try { return Format(Formula.Evaluate(this, raw[1..], row, column), full); }
        catch (FormulaException e) { return e.Code; }
    }

    /// <summary>A result as text: seven digits fit a cell, and twelve go to a file or another tool.</summary>
    public static string Format(double v, bool full = false) => double.IsFinite(v) ? (Math.Abs(v) < 1e-10 ? 0 : v).ToString(full ? "G12" : "G7", Ci) : "#NUM!";   // cos(90°) is 1e-15 in floating point, which is a zero to a person

    /// <summary>The cell as a number, or null when it is empty, text or an error.</summary>
    public double? Number(int row, int column)
    {
        try { return NumberOrThrow(row, column, 0); } catch (FormulaException) { return null; }
    }

    /// <summary>The number in a cell. An error in a formula, or in a cell it reads, is thrown so it reaches the cell that shows it.</summary>
    internal double? NumberOrThrow(int row, int column, int depth)
    {
        string raw = Raw(row, column).Trim();
        if (raw.Length == 0) return null;
        if (raw.StartsWith('=')) return Formula.Evaluate(this, raw[1..], row, column, depth + 1);
        return double.TryParse(raw.Replace(" ", ""), NumberStyles.Float, Ci, out var v) && double.IsFinite(v) ? v : null;
    }

    /// <summary>Rows whose first columns are all numbers, as numbers. A row of names above the data, or a note, is skipped.</summary>
    public List<double[]> NumericRows(int columns, int maxColumns = 0)
    {
        var res = new List<double[]>();
        int width = maxColumns > columns ? maxColumns : columns;
        for (int r = 0; r < Rows.Count; r++)
        {
            var values = new List<double>();
            for (int c = 0; c < width; c++)
            {
                var n = Number(r, c);
                if (n == null) break;
                values.Add(n.Value);
            }
            if (values.Count >= columns) res.Add([.. values]);
        }
        return res;
    }

    // ------------------------------------------------------------------ text in and out
    public string ToTsv() => string.Join("\n", Enumerable.Range(0, Rows.Count).Select(r => string.Join("\t", Enumerable.Range(0, ColumnCount).Select(c => Display(r, c, true)))));

    public string ToCsv()
    {
        static string Quote(string s) => s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        return string.Join("\r\n", Enumerable.Range(0, Rows.Count).Select(r => string.Join(",", Enumerable.Range(0, ColumnCount).Select(c => Quote(Display(r, c, true))))));
    }

    /// <summary>Splits pasted or loaded text into cells. The separator is found from the text: tabs (what a spreadsheet copies), semicolons, commas, or runs of spaces. Quoted cells may hold the separator.</summary>
    public static List<List<string>> Parse(string text)
    {
        text = text.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        var lines = SplitLines(text);
        char? sep = lines.Any(l => l.Contains('\t')) ? '\t' : lines.Any(l => l.Contains(';')) ? ';' : lines.Any(l => l.Contains(',')) ? ',' : null;
        var rows = new List<List<string>>();
        foreach (var line in lines)
        {
            if (sep == null) rows.Add(line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).ToList());
            else rows.Add(SplitCells(line, sep.Value));
        }
        return rows;
    }

    /// <summary>Lines, where a quote that is still open carries on to the next line.</summary>
    static List<string> SplitLines(string text)
    {
        var lines = new List<string>(); var sb = new StringBuilder(); bool quoted = false;
        foreach (char c in text)
        {
            if (c == '"') quoted = !quoted;
            if (c == '\n' && !quoted) { lines.Add(sb.ToString()); sb.Clear(); } else sb.Append(c);
        }
        if (sb.Length > 0 || lines.Count == 0) lines.Add(sb.ToString());
        return lines;
    }

    static List<string> SplitCells(string line, char sep)
    {
        var cells = new List<string>(); var sb = new StringBuilder(); bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"' && sb.Length == 0) quoted = true;
            else if (c == sep) { cells.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(c);
        }
        cells.Add(sb.ToString().Trim());
        return cells;
    }

    public static DataSheet FromText(string name, string text) => new() { Name = name, Rows = Parse(text) };

    /// <summary>The sheets in a file: one for a text file (CSV, TSV, TXT, any plain table), one for each sheet of an Excel workbook.</summary>
    public static List<DataSheet> FromFile(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase)) return ReadWorkbook(path, name);
        if (path.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Old .xls files are not supported. In Excel, Save As .xlsx or .csv, then open that.");
        return [FromText(name, File.ReadAllText(path))];
    }

    // ------------------------------------------------------------------ Excel (.xlsx): a zip of XML files
    static List<DataSheet> ReadWorkbook(string path, string fallback)
    {
        using var zip = ZipFile.OpenRead(path);
        XDocument Part(string name) { var e = zip.GetEntry(name) ?? throw new InvalidDataException($"This is not a workbook: {name} is missing."); using var s = e.Open(); return XDocument.Load(s); }
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main", rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships", pkg = "http://schemas.openxmlformats.org/package/2006/relationships";
        var strings = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") != null)
            foreach (var si in Part("xl/sharedStrings.xml").Root!.Elements(main + "si")) strings.Add(string.Concat(si.Descendants(main + "t").Select(t => t.Value)));
        var targets = Part("xl/_rels/workbook.xml.rels").Root!.Elements(pkg + "Relationship").ToDictionary(r => (string)r.Attribute("Id")!, r => (string)r.Attribute("Target")!);
        var sheets = new List<DataSheet>();
        foreach (var sheet in Part("xl/workbook.xml").Root!.Descendants(main + "sheet"))
        {
            string target = targets[(string)sheet.Attribute(rel + "id")!];
            string entry = target.StartsWith('/') ? target[1..] : "xl/" + target;
            if (zip.GetEntry(entry) == null) continue;
            var table = new DataSheet { Name = (string?)sheet.Attribute("name") ?? fallback };
            foreach (var cell in Part(entry).Descendants(main + "c"))
            {
                string reference = (string?)cell.Attribute("r") ?? "";
                int split = reference.TakeWhile(char.IsLetter).Count();
                int column = ColumnIndex(reference[..split]), row = int.TryParse(reference[split..], out var rn) ? rn - 1 : -1;
                if (column < 0 || row < 0) continue;
                string type = (string?)cell.Attribute("t") ?? "n";
                string value = cell.Element(main + "v")?.Value ?? "";
                string text = type switch
                {
                    "s" => int.TryParse(value, out var si) && si < strings.Count ? strings[si] : "",
                    "inlineStr" => string.Concat(cell.Descendants(main + "t").Select(t => t.Value)),
                    "b" => value == "1" ? "TRUE" : "FALSE",
                    _ => value,
                };
                if (text.Length > 0) table.Set(row, column, text);
            }
            sheets.Add(table);
        }
        if (sheets.Count == 0) throw new InvalidDataException("That workbook has no sheets.");
        return sheets;
    }
}

public sealed class FormulaException(string code) : Exception(code) { public string Code { get; } = code; }

/// <summary>A small spreadsheet formula evaluator: numbers, cell references (B3), ranges (A1:A9), + - * / ^, brackets and the usual functions.</summary>
static class Formula
{
    public static double Evaluate(DataSheet sheet, string text, int row, int column, int depth = 0)
    {
        if (depth > 40) throw new FormulaException("#CYCLE!");
        var p = new Parser(sheet, text, depth);
        var v = p.Sum();
        p.End();
        return v;
    }

    sealed class Parser(DataSheet sheet, string text, int depth)
    {
        int _i;
        char Peek() { while (_i < text.Length && text[_i] == ' ') _i++; return _i < text.Length ? text[_i] : '\0'; }
        public void End() { if (Peek() != '\0') throw new FormulaException("#NAME?"); }

        public double Sum()
        {
            double v = Product();
            for (char c = Peek(); c is '+' or '-'; c = Peek()) { _i++; double r = Product(); v = c == '+' ? v + r : v - r; }
            return v;
        }

        double Product()
        {
            double v = Power();
            for (char c = Peek(); c is '*' or '/'; c = Peek())
            {
                _i++; double r = Power();
                if (c == '/' && r == 0) throw new FormulaException("#DIV/0!");
                v = c == '*' ? v * r : v / r;
            }
            return v;
        }

        double Power()
        {
            double v = Unary();
            if (Peek() == '^') { _i++; v = Math.Pow(v, Power()); }
            return v;
        }

        double Unary()
        {
            char c = Peek();
            if (c == '-') { _i++; return -Unary(); }
            if (c == '+') { _i++; return Unary(); }
            return Primary();
        }

        double Primary()
        {
            char c = Peek();
            if (c == '(') { _i++; double v = Sum(); if (Peek() != ')') throw new FormulaException("#NAME?"); _i++; return v; }
            if (char.IsDigit(c) || c == '.')
            {
                int s = _i;
                while (_i < text.Length && (char.IsDigit(text[_i]) || text[_i] == '.' || ((text[_i] is 'e' or 'E') && _i + 1 < text.Length && (char.IsDigit(text[_i + 1]) || text[_i + 1] is '-' or '+')) || (_i > s && text[_i] is '-' or '+' && text[_i - 1] is 'e' or 'E'))) _i++;
                return double.Parse(text[s.._i], CultureInfo.InvariantCulture);
            }
            if (char.IsLetter(c))
            {
                int s = _i;
                while (_i < text.Length && (char.IsLetterOrDigit(text[_i]) || text[_i] == '_')) _i++;
                string name = text[s.._i].ToUpperInvariant();
                if (Peek() == '(') { _i++; var args = Arguments(); return Call(name, args); }
                if (TryCell(name, out int r, out int col))
                {
                    if (Peek() == ':') { _i++; throw new FormulaException("#VALUE!"); }       // a range only makes sense inside a function
                    return CellValue(r, col);
                }
                if (name == "PI") return Math.PI;
                throw new FormulaException("#NAME?");
            }
            throw new FormulaException("#NAME?");
        }

        double CellValue(int r, int c)
        {
            string raw = sheet.Raw(r, c).Trim();
            if (raw.Length == 0) return 0;
            return sheet.NumberOrThrow(r, c, depth) ?? throw new FormulaException("#VALUE!");
        }

        static bool TryCell(string name, out int row, out int column)
        {
            row = column = -1;
            int letters = name.TakeWhile(char.IsLetter).Count();
            if (letters == 0 || letters == name.Length || !int.TryParse(name[letters..], out int n) || n < 1) return false;
            column = DataSheet.ColumnIndex(name[..letters]); row = n - 1;
            return column >= 0;
        }

        /// <summary>Each argument as numbers: a plain value is one, a range is all its numeric cells.</summary>
        List<double> Arguments()
        {
            var list = new List<double>();
            if (Peek() == ')') { _i++; return list; }
            while (true)
            {
                int save = _i;
                if (char.IsLetter(Peek()))
                {
                    int s = _i; while (_i < text.Length && char.IsLetterOrDigit(text[_i])) _i++;
                    if (TryCell(text[s.._i].ToUpperInvariant(), out int r1, out int c1) && Peek() == ':')
                    {
                        _i++; int s2 = _i; while (_i < text.Length && char.IsLetterOrDigit(text[_i])) _i++;
                        if (!TryCell(text[s2.._i].ToUpperInvariant(), out int r2, out int c2)) throw new FormulaException("#NAME?");
                        for (int r = Math.Min(r1, r2); r <= Math.Max(r1, r2); r++) for (int c = Math.Min(c1, c2); c <= Math.Max(c1, c2); c++)
                            if (sheet.Raw(r, c).Trim().Length > 0 && sheet.NumberOrThrow(r, c, depth) is { } v) list.Add(v);
                    }
                    else { _i = save; list.Add(Sum()); }
                }
                else list.Add(Sum());
                char c3 = Peek();
                if (c3 == ',' || c3 == ';') { _i++; continue; }
                if (c3 == ')') { _i++; return list; }
                throw new FormulaException("#NAME?");
            }
        }

        static double Call(string name, List<double> a)
        {
            double One() => a.Count == 1 ? a[0] : throw new FormulaException("#VALUE!");
            switch (name)
            {
                case "SUM": return a.Sum();
                case "AVERAGE": return a.Count == 0 ? throw new FormulaException("#DIV/0!") : a.Average();
                case "MIN": return a.Count == 0 ? 0 : a.Min();
                case "MAX": return a.Count == 0 ? 0 : a.Max();
                case "COUNT": return a.Count;
                case "ABS": return Math.Abs(One());
                case "SQRT": return One() < 0 ? throw new FormulaException("#NUM!") : Math.Sqrt(One());
                case "SIN": return Math.Sin(One());
                case "COS": return Math.Cos(One());
                case "TAN": return Math.Tan(One());
                case "ATAN2": return a.Count == 2 ? Math.Atan2(a[0], a[1]) : throw new FormulaException("#VALUE!");
                case "RADIANS": return One() * Math.PI / 180;
                case "DEGREES": return One() * 180 / Math.PI;
                case "EXP": return Math.Exp(One());
                case "LN": return One() <= 0 ? throw new FormulaException("#NUM!") : Math.Log(One());
                case "POWER": return a.Count == 2 ? Math.Pow(a[0], a[1]) : throw new FormulaException("#VALUE!");
                case "ROUND": return a.Count is 1 or 2 ? Math.Round(a[0], a.Count == 2 ? (int)a[1] : 0, MidpointRounding.AwayFromZero) : throw new FormulaException("#VALUE!");
                case "PI": return Math.PI;
                default: throw new FormulaException("#NAME?");
            }
        }
    }
}

/// <summary>How a tool reads a table: typed rows, a file, or a sheet of this document named with an @.</summary>
public static class DataSource
{
    /// <summary>True when the text points at a table elsewhere (a sheet such as @Cam, or a file) instead of holding rows.</summary>
    public static bool IsReference(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return false;
        if (text.StartsWith('@')) return true;
        return text.IndexOfAny(['\n', ';']) < 0 && text.IndexOfAny(Path.GetInvalidPathChars()) < 0 && File.Exists(text);
    }

    /// <summary>The rows behind a reference as plain text, or the text itself when it already holds rows.</summary>
    public static string Expand(CadSession? session, string text)
    {
        string t = text.Trim();
        if (t.StartsWith('@'))
        {
            string name = t[1..].Trim();
            var sheet = session?.DataSheets.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new FormatException(session == null || session.DataSheets.Count == 0
                    ? $"There is no sheet called {name}: the Data panel has no sheets yet."
                    : $"There is no sheet called {name}. The sheets are: {string.Join(", ", session.DataSheets.Select(s => s.Name))}.");
            return sheet.ToTsv();
        }
        if (IsReference(t)) return DataSheet.FromFile(t)[0].ToTsv();
        return text;
    }

    /// <summary>Drops the lines at the top that hold no number at all: a row of names above the data.</summary>
    public static string WithoutHeaders(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        while (lines.Count > 0 && !lines[0].Any(char.IsDigit)) lines.RemoveAt(0);
        return string.Join("\n", lines);
    }
}
