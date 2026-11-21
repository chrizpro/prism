using Prism.Core.Data;
using Xunit;

namespace Prism.UnitTests.Data;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/curves/README.md#the-data-panel">The Data panel.</seealso>
[Trait("Feature", "Data")]
[Trait("Layer", "Unit")]
public class DataSheetEditTests
{
    static DataSheet Column(params string[] values) => new() { Rows = values.Select(v => new List<string> { v }).ToList() };

    [Theory(DisplayName = "DAT-U001 | A copied formula moves its references by the rows and columns it moved; $ pins one")]
    [InlineData("=A1*2", 1, 0, "=A2*2")]
    [InlineData("=A1+B1", 0, 1, "=B1+C1")]
    [InlineData("=SUM(A1:A3)", 3, 0, "=SUM(A4:A6)")]
    [InlineData("=$A1*B$2", 2, 1, "=$A3*C$2")]
    [InlineData("=A2", -1, 0, "=A1")]
    [InlineData("hello", 5, 5, "hello")]
    [Trait("TestId", "DAT-U001")]
    public void MoveFormula(string raw, int rows, int columns, string expected) => Assert.Equal(expected, DataSheet.MoveFormula(raw, rows, columns));

    [Fact(DisplayName = "DAT-U002 | Sorting orders numbers as numbers, keeps a row of names on top and leaves empty cells last")]
    [Trait("TestId", "DAT-U002")]
    public void SortKeepsHeaderAndEmptyLast()
    {
        var sheet = Column("x", "10", "2", "", "33", "1");
        sheet.SortBy(0, true, 1);
        Assert.Equal(["x", "1", "2", "10", "33", ""], sheet.Rows.Select(r => r[0]));
        sheet.SortBy(0, false, 1);
        Assert.Equal(["x", "33", "10", "2", "1", ""], sheet.Rows.Select(r => r[0]));
    }

    [Fact(DisplayName = "DAT-U003 | Sorting moves whole rows, so values in other columns stay with their row")]
    [Trait("TestId", "DAT-U003")]
    public void SortMovesRows()
    {
        var sheet = new DataSheet { Rows = [["3", "c"], ["1", "a"], ["2", "b"]] };
        sheet.SortBy(0, true);
        Assert.Equal(["a", "b", "c"], sheet.Rows.Select(r => r[1]));
    }

    [Fact(DisplayName = "DAT-U004 | Fill down repeats the top cell, moving its formula; two typed numbers continue as a series")]
    [Trait("TestId", "DAT-U004")]
    public void FillDown()
    {
        var formula = Column("=A5*2", "", "", "");
        formula.Set(4, 0, "7");
        formula.FillDown(0, 0, 3, 0);
        Assert.Equal(["=A5*2", "=A6*2", "=A7*2", "=A8*2"], formula.Rows.Take(4).Select(r => r[0]));

        var series = Column("1", "2", "", "", "");
        series.FillDown(0, 0, 4, 0);
        Assert.Equal(["1", "2", "3", "4", "5"], series.Rows.Select(r => r[0]));

        var repeat = Column("5", "", "");
        repeat.FillDown(0, 0, 2, 0);
        Assert.Equal(["5", "5", "5"], repeat.Rows.Select(r => r[0]));
    }

    [Fact(DisplayName = "DAT-U005 | Statistics of a block count only its numbers; a block with none gives nothing")]
    [Trait("TestId", "DAT-U005")]
    public void Stats()
    {
        var sheet = new DataSheet { Rows = [["a", "4"], ["2", "=A2*3"], ["", "x"]] };
        var s = sheet.Stats(0, 0, 2, 1)!.Value;
        Assert.Equal(3, s.count); Assert.Equal(12, s.sum); Assert.Equal(2, s.min); Assert.Equal(6, s.max);
        Assert.Null(sheet.Stats(2, 1, 2, 1));
    }

    [Fact(DisplayName = "DAT-U006 | A snapshot is independent of later edits, so undo can put the cells back")]
    [Trait("TestId", "DAT-U006")]
    public void SnapshotIsIndependent()
    {
        var sheet = Column("1", "2");
        var before = sheet.Snapshot();
        sheet.Set(0, 0, "99");
        Assert.Equal("1", before[0][0]);
    }
}
