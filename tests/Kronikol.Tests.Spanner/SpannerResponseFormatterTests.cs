using Google.Cloud.Spanner.V1;
using Google.Protobuf.WellKnownTypes;
using Kronikol.Extensions.Spanner;
using Type = Google.Cloud.Spanner.V1.Type;
using TypeCode = Google.Cloud.Spanner.V1.TypeCode;

namespace Kronikol.Tests.Spanner;

public class SpannerResponseFormatterTests
{
    // ─── FormatResultSet ────────────────────────────────────

    [Fact]
    public void FormatResultSet_EmptyResult_Returns0Rows()
    {
        var rs = new ResultSet
        {
            Metadata = new ResultSetMetadata
            {
                RowType = new StructType
                {
                    Fields = { new StructType.Types.Field { Name = "Id", Type = new Type { Code = TypeCode.Int64 } } }
                }
            }
        };

        var result = SpannerResponseFormatter.FormatResultSet(rs, SpannerResponseDetail.RowCountOnly, 5);

        Assert.Equal("0 rows", result);
    }

    [Fact]
    public void FormatResultSet_RowCountOnly_ReturnsCountOnly()
    {
        var rs = MakeResultSet(["Name", "Age"], [["Alice", "30"], ["Bob", "25"]]);

        var result = SpannerResponseFormatter.FormatResultSet(rs, SpannerResponseDetail.RowCountOnly, 5);

        Assert.Equal("2 rows", result);
    }

    [Fact]
    public void FormatResultSet_SingleRow_ReturnsSingular()
    {
        var rs = MakeResultSet(["Name"], [["Alice"]]);

        var result = SpannerResponseFormatter.FormatResultSet(rs, SpannerResponseDetail.RowCountOnly, 5);

        Assert.Equal("1 row", result);
    }

    [Fact]
    public void FormatResultSet_RowCountAndColumns_ReturnsCountWithColumnNames()
    {
        var rs = MakeResultSet(["Name", "Age"], [["Alice", "30"], ["Bob", "25"]]);

        var result = SpannerResponseFormatter.FormatResultSet(rs, SpannerResponseDetail.RowCountAndColumns, 5);

        Assert.Equal("2 rows [Name, Age]", result);
    }

    [Fact]
    public void FormatResultSet_FullRows_ReturnsRowData()
    {
        var rs = MakeResultSet(["Name", "Age"], [["Alice", "30"], ["Bob", "25"]]);

        var result = SpannerResponseFormatter.FormatResultSet(rs, SpannerResponseDetail.FullRows, 5);

        Assert.DoesNotContain("2 rows", result);
        Assert.Contains("Alice", result);
        Assert.Contains("Bob", result);
    }

    [Fact]
    public void FormatResultSet_MaxRowsTruncates()
    {
        var rs = MakeResultSet(["Name"], [["Alice"], ["Bob"], ["Carol"], ["Dan"], ["Eve"]]);

        var result = SpannerResponseFormatter.FormatResultSet(rs, SpannerResponseDetail.FullRows, 2);

        Assert.Contains("Alice", result);
        Assert.Contains("Bob", result);
        Assert.DoesNotContain("Carol", result);
        Assert.Contains("... (3 more rows not shown)", result);
    }

    [Fact]
    public void FormatResultSet_MaxRowsZero_ShowsCountOnly()
    {
        var rs = MakeResultSet(["Name"], [["Alice"], ["Bob"]]);

        var result = SpannerResponseFormatter.FormatResultSet(rs, SpannerResponseDetail.FullRows, 0);

        Assert.Equal("2 rows [Name]", result);
    }

    [Fact]
    public void FormatResultSet_NullValues_HandledGracefully()
    {
        var rs = new ResultSet
        {
            Metadata = new ResultSetMetadata
            {
                RowType = new StructType
                {
                    Fields =
                    {
                        new StructType.Types.Field { Name = "Name", Type = new Type { Code = TypeCode.String } },
                        new StructType.Types.Field { Name = "Age", Type = new Type { Code = TypeCode.Int64 } }
                    }
                }
            },
            Rows =
            {
                new ListValue
                {
                    Values =
                    {
                        Value.ForString("Alice"),
                        Value.ForNull()
                    }
                }
            }
        };

        var result = SpannerResponseFormatter.FormatResultSet(rs, SpannerResponseDetail.FullRows, 5);

        Assert.Contains("null", result);
    }

    [Fact]
    public void FormatResultSet_RowCountAndColumns_ManyColumns_Truncated()
    {
        var columns = Enumerable.Range(1, 25).Select(i => $"Col{i}").ToArray();
        var rs = MakeResultSet(columns, [columns.Select(_ => "val").ToArray()]);

        var result = SpannerResponseFormatter.FormatResultSet(rs, SpannerResponseDetail.RowCountAndColumns, 5);

        Assert.Contains("Col1", result);
        Assert.Contains("Col20", result);
        Assert.DoesNotContain("Col21", result);
        Assert.Contains("... (+5 more)", result);
    }

    [Fact]
    public void FormatResultSet_FullRows_LargeValues_Truncated()
    {
        var largeValue = new string('x', 600);
        var rs = MakeResultSet(["Data"], [[largeValue]]);

        var result = SpannerResponseFormatter.FormatResultSet(rs, SpannerResponseDetail.FullRows, 5);

        Assert.True(result!.Length < largeValue.Length);
        Assert.Contains("...", result);
    }

    [Fact]
    public void FormatResultSet_FullRows_MixedTypes_FormatsCorrectly()
    {
        var rs = new ResultSet
        {
            Metadata = new ResultSetMetadata
            {
                RowType = new StructType
                {
                    Fields =
                    {
                        new StructType.Types.Field { Name = "Name", Type = new Type { Code = TypeCode.String } },
                        new StructType.Types.Field { Name = "Score", Type = new Type { Code = TypeCode.Float64 } },
                        new StructType.Types.Field { Name = "Active", Type = new Type { Code = TypeCode.Bool } }
                    }
                }
            },
            Rows =
            {
                new ListValue
                {
                    Values =
                    {
                        Value.ForString("Alice"),
                        Value.ForNumber(99.5),
                        Value.ForBool(true)
                    }
                }
            }
        };

        var result = SpannerResponseFormatter.FormatResultSet(rs, SpannerResponseDetail.FullRows, 5);

        Assert.Contains("Alice", result);
        Assert.Contains("99.5", result);
        Assert.Contains("true", result);
    }

    [Theory]
    [MemberData(nameof(CultureRun.Data), MemberType = typeof(CultureRun))]
    public void FormatResultSet_FullRows_writes_a_float_the_same_on_every_machine(string culture)
    {
        var rs = new ResultSet
        {
            Metadata = new ResultSetMetadata
            {
                RowType = new StructType
                {
                    Fields =
                    {
                        new StructType.Types.Field { Name = "Score", Type = new Type { Code = TypeCode.Float64 } },
                        new StructType.Types.Field { Name = "Delta", Type = new Type { Code = TypeCode.Float64 } }
                    }
                }
            },
            Rows = { new ListValue { Values = { Value.ForNumber(1234.5), Value.ForNumber(-0.25) } } }
        };

        var result = CultureRun.Under(culture, () => SpannerResponseFormatter.FormatResultSet(rs, SpannerResponseDetail.FullRows, 5));
        var expected = CultureRun.Under("en-US", () => SpannerResponseFormatter.FormatResultSet(rs, SpannerResponseDetail.FullRows, 5));

        Assert.Contains("1234.5", result);
        Assert.Contains("-0.25", result);
        Assert.Equal(expected, result);
    }

    // ─── FormatCommitResponse ───────────────────────────────

    [Fact]
    public void FormatCommitResponse_ReturnsTimestamp()
    {
        var cr = new CommitResponse
        {
            CommitTimestamp = Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero))
        };

        var result = SpannerResponseFormatter.FormatCommitResponse(cr);

        Assert.Contains("2026-05-15", result);
    }

    [Fact]
    public void FormatCommitResponse_NullTimestamp_ReturnsCommitted()
    {
        var cr = new CommitResponse();

        var result = SpannerResponseFormatter.FormatCommitResponse(cr);

        Assert.Equal("Committed", result);
    }

    // ─── FormatBatchDmlResponse ─────────────────────────────

    [Fact]
    public void FormatBatchDmlResponse_ReturnsPerStatementStats()
    {
        var response = new ExecuteBatchDmlResponse();
        response.ResultSets.Add(new ResultSet
        {
            Stats = new ResultSetStats { RowCountExact = 2 }
        });
        response.ResultSets.Add(new ResultSet
        {
            Stats = new ResultSetStats { RowCountExact = 1 }
        });

        var result = SpannerResponseFormatter.FormatBatchDmlResponse(response, SpannerResponseDetail.RowCountAndColumns);

        Assert.Contains("2 statements", result);
    }

    // ─── FormatPartialResultSets ────────────────────────────

    [Fact]
    public void FormatPartialResultSets_EmptyChunks_ReturnsNull()
    {
        var result = SpannerResponseFormatter.FormatPartialResultSets(
            [], SpannerResponseDetail.RowCountAndColumns, 5);

        Assert.Null(result);
    }

    [Fact]
    public void FormatPartialResultSets_AccumulatesChunks()
    {
        var chunk1 = new PartialResultSet
        {
            Metadata = new ResultSetMetadata
            {
                RowType = new StructType
                {
                    Fields =
                    {
                        new StructType.Types.Field { Name = "Name", Type = new Type { Code = TypeCode.String } },
                        new StructType.Types.Field { Name = "Age", Type = new Type { Code = TypeCode.String } }
                    }
                }
            },
            Values = { Value.ForString("Alice"), Value.ForString("30") }
        };
        var chunk2 = new PartialResultSet
        {
            Values = { Value.ForString("Bob"), Value.ForString("25") }
        };

        var result = SpannerResponseFormatter.FormatPartialResultSets(
            [chunk1, chunk2], SpannerResponseDetail.RowCountAndColumns, 5);

        Assert.Contains("2 rows", result);
        Assert.Contains("Name", result);
    }

    [Fact]
    public void FormatPartialResultSets_ChunkedValues_CountsRowsCorrectly()
    {
        // 3 columns, 6 values across 2 chunks = 2 rows
        var chunk1 = new PartialResultSet
        {
            Metadata = new ResultSetMetadata
            {
                RowType = new StructType
                {
                    Fields =
                    {
                        new StructType.Types.Field { Name = "A", Type = new Type { Code = TypeCode.String } },
                        new StructType.Types.Field { Name = "B", Type = new Type { Code = TypeCode.String } },
                        new StructType.Types.Field { Name = "C", Type = new Type { Code = TypeCode.String } }
                    }
                }
            },
            Values = { Value.ForString("1"), Value.ForString("2"), Value.ForString("3"), Value.ForString("4") }
        };
        var chunk2 = new PartialResultSet
        {
            Values = { Value.ForString("5"), Value.ForString("6") }
        };

        var result = SpannerResponseFormatter.FormatPartialResultSets(
            [chunk1, chunk2], SpannerResponseDetail.RowCountOnly, 5);

        Assert.Equal("2 rows", result);
    }

    // ─── FormatRaw ──────────────────────────────────────────

    [Fact]
    public void FormatRaw_IMessage_ReturnsJsonFormat()
    {
        var rs = new ResultSet();

        var result = SpannerResponseFormatter.FormatRaw(rs);

        Assert.NotNull(result);
    }

    [Fact]
    public void FormatRaw_NonMessage_ReturnsNull()
    {
        var result = SpannerResponseFormatter.FormatRaw("not a protobuf");

        Assert.Null(result);
    }

    // ─── MaxResponseRows negative ───────────────────────────

    [Fact]
    public void FormatResultSet_NegativeMaxRows_TreatedAsZero()
    {
        var rs = MakeResultSet(["Name"], [["Alice"], ["Bob"]]);

        var result = SpannerResponseFormatter.FormatResultSet(rs, SpannerResponseDetail.FullRows, -1);

        Assert.Equal("2 rows [Name]", result);
    }

    // ─── Helpers ────────────────────────────────────────────

    private static ResultSet MakeResultSet(string[] columns, string[][] rows)
    {
        var rs = new ResultSet
        {
            Metadata = new ResultSetMetadata
            {
                RowType = new StructType()
            }
        };

        foreach (var col in columns)
        {
            rs.Metadata.RowType.Fields.Add(new StructType.Types.Field
            {
                Name = col,
                Type = new Type { Code = TypeCode.String }
            });
        }

        foreach (var row in rows)
        {
            var lv = new ListValue();
            foreach (var val in row)
                lv.Values.Add(Value.ForString(val));
            rs.Rows.Add(lv);
        }

        return rs;
    }
}
