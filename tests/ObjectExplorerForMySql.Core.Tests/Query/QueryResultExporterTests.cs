using System;
using System.IO;
using System.Text.Json;
using ObjectExplorerForMySql.Core.Query;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Query;

public class QueryResultExporterTests
{
    private static readonly QueryResultSet _set = new(
        ["id", "name", "price", "added", "picture", "id"],
        [
            [1, "Plain", 1.50m, new DateTime(2026, 10, 30, 14, 5, 0), new byte[] { 0x0A, 0xFF }, 7],
            [2, "Comma, \"quote\"\nline", null, "2026-10-30", null, 8L],
        ],
        totalRows: 2);

    private static string Csv(QueryResultSet set)
    {
        using var writer = new StringWriter();
        QueryResultExporter.WriteCsv(set, writer);
        return writer.ToString();
    }

    private static string Json(QueryResultSet set)
    {
        using var writer = new StringWriter();
        QueryResultExporter.WriteJson(set, writer);
        return writer.ToString();
    }

    [Fact]
    public void Csv_HeaderQuotingAndNull()
    {
        Assert.Equal(
            "id,name,price,added,picture,id\r\n" +
            "1,Plain,1.50,2026-10-30 14:05:00,0x0AFF,7\r\n" +
            "2,\"Comma, \"\"quote\"\"\nline\",,2026-10-30,,8\r\n",
            Csv(_set));
    }

    [Fact]
    public void Csv_BinaryIsWrittenInFull()
    {
        var set = new QueryResultSet(["b"], [[new byte[100]]], 1);

        Assert.Equal("b\r\n0x" + new string('0', 200) + "\r\n", Csv(set));
    }

    [Fact]
    public void Json_IsValid_WithNumbersNullsAndUniqueNames()
    {
        using var document = JsonDocument.Parse(Json(_set));
        var rows = document.RootElement;

        Assert.Equal(2, rows.GetArrayLength());
        Assert.Equal(1, rows[0].GetProperty("id").GetInt32());
        Assert.Equal(7, rows[0].GetProperty("id_2").GetInt32());
        Assert.Equal(1.50m, rows[0].GetProperty("price").GetDecimal());
        Assert.Equal("2026-10-30 14:05:00", rows[0].GetProperty("added").GetString());
        Assert.Equal("0x0AFF", rows[0].GetProperty("picture").GetString());
        Assert.Equal("Comma, \"quote\"\nline", rows[1].GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("price").ValueKind);
    }

    [Fact]
    public void Json_EmptyResult_IsAnEmptyArray()
    {
        Assert.Equal("[]\n", Json(new QueryResultSet(["a"], [], 0)));
    }

    [Fact]
    public void Json_EscapesControlCharacters()
    {
        var json = Json(new QueryResultSet(["t"], [["a\tb\u0001"]], 1));

        Assert.Contains("\"a\\tb\\u0001\"", json, StringComparison.Ordinal);
        Assert.Equal("a\tb\u0001", JsonDocument.Parse(json).RootElement[0].GetProperty("t").GetString());
    }
}
