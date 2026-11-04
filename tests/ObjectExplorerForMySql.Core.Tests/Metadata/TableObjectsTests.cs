using System.Linq;
using ObjectExplorerForMySql.Core.Metadata;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Metadata;

public class TableObjectsTests
{
    [Fact]
    public void Keys_FromRows_GroupsOrdersAndLabels()
    {
        var keys = KeyInfo.FromRows(
        [
            new KeyInfo.Row("fk_order_customer", "FOREIGN KEY", "customer_id", 1, "shop", "customers", "id"),
            new KeyInfo.Row("uq_number", "UNIQUE", "number", 1, null, null, null),
            new KeyInfo.Row("PRIMARY", "PRIMARY KEY", "line", 2, null, null, null),
            new KeyInfo.Row("PRIMARY", "PRIMARY KEY", "order_id", 1, null, null, null),
            new KeyInfo.Row("fk_order_country", "FOREIGN KEY", "country", 1, "geo", "countries", "code"),
            new KeyInfo.Row("fk_order_product", "FOREIGN KEY", "region", 2, "shop", "products", "region"),
            new KeyInfo.Row("fk_order_product", "FOREIGN KEY", "sku", 1, "shop", "products", "sku"),
        ]);

        Assert.Equal(
            [
                "PRIMARY (order_id, line)",
                "uq_number (number)",
                "fk_order_country (country) → geo.countries (code)",
                "fk_order_customer (customer_id) → customers (id)",
                "fk_order_product (sku, region) → products (sku, region)",
            ],
            keys.Select(k => k.LabelFor("shop")));
        Assert.Equal([KeyType.Primary, KeyType.Unique, KeyType.Foreign, KeyType.Foreign, KeyType.Foreign], keys.Select(k => k.Type));
    }

    [Fact]
    public void Indexes_FromRows_PrimaryFirstWithPrefixAndExpression()
    {
        var indexes = IndexInfo.FromRows(
        [
            new IndexInfo.Row("ix_name", true, "name", 1, "BTREE", 10),
            new IndexInfo.Row("ft_body", true, "body", 1, "FULLTEXT", null),
            new IndexInfo.Row("PRIMARY", false, "id", 1, "BTREE", null),
            new IndexInfo.Row("uq_email", false, "email", 1, "BTREE", null),
            new IndexInfo.Row("ix_func", true, null, 1, "BTREE", null),
            new IndexInfo.Row("ix_name", true, "city", 2, "BTREE", null),
        ]);

        Assert.Equal(
            [
                "PRIMARY (unique: id)",
                "ft_body (fulltext: body)",
                "ix_func (non-unique: (expression))",
                "ix_name (non-unique: name(10), city)",
                "uq_email (unique: email)",
            ],
            indexes.Select(i => i.Label));
        Assert.True(indexes[0].IsPrimary);
        Assert.True(indexes[1].IsFullText);
    }

    [Theory]
    [InlineData(true, "chk_price ((`price` >= 0))")]
    [InlineData(false, "chk_price ((`price` >= 0), not enforced)")]
    public void CheckConstraint_Label(bool enforced, string expected)
    {
        Assert.Equal(expected, new CheckConstraintInfo("chk_price", "(`price` >= 0)", enforced).Label);
    }

    [Fact]
    public void CheckConstraint_Label_ShortensLongClauses()
    {
        var label = new CheckConstraintInfo("chk", new string('x', 200), true).Label;

        Assert.EndsWith("...)", label);
        Assert.True(label.Length < 70);
    }

    [Fact]
    public void Trigger_Label()
    {
        Assert.Equal("trg_orders_bi (BEFORE INSERT)", new TriggerInfo("trg_orders_bi", "before", "insert").Label);
    }

    [Theory]
    [InlineData("8.0.36", false, true)]
    [InlineData("8.0.16", false, true)]
    [InlineData("8.0.15", false, false)]
    [InlineData("8.4.2-log", false, true)]
    [InlineData("9.1.0", false, true)]
    [InlineData("10.11.6-MariaDB-0ubuntu0.24.04.1", true, true)]
    [InlineData("10.1.48-MariaDB", true, false)]
    [InlineData(null, false, false)]
    [InlineData("garbage", false, false)]
    public void ServerCapabilities_FromVersion(string? version, bool mariaDb, bool checks)
    {
        var capabilities = ServerCapabilities.FromVersion(version);

        Assert.Equal(mariaDb, capabilities.IsMariaDb);
        Assert.Equal(checks, capabilities.SupportsCheckConstraints);
    }
}
