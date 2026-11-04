using ObjectExplorerForMySql.Core.Security;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Security;

public class SensitiveDataMaskerTests
{
    [Theory]
    [InlineData(
        "Server=localhost;Port=3306;User ID=root;Password=Secret123;Database=shop",
        "Server=localhost;Port=3306;User ID=root;Password=*****;Database=shop")]
    [InlineData("server=db;pwd=Secret123", "server=db;pwd=*****")]
    [InlineData("Server=db;PASSWORD=Secret123", "Server=db;PASSWORD=*****")]
    [InlineData("Server=db; Password = Secret123 ;Port=3306", "Server=db; Password = *****;Port=3306")]
    [InlineData("Server=db;User Password=Secret123", "Server=db;User Password=*****")]
    [InlineData("Server=db;Passwd=Secret123", "Server=db;Passwd=*****")]
    [InlineData("Server=db;CertificatePassword=Secret123;SslMode=Required", "Server=db;CertificatePassword=*****;SslMode=Required")]
    [InlineData("Server=db;Password=Secret123", "Server=db;Password=*****")]
    [InlineData("Password=Secret123;Server=db", "Password=*****;Server=db")]
    public void MaskConnectionString_MasksPasswordValues(string input, string expected)
    {
        Assert.Equal(expected, SensitiveDataMasker.MaskConnectionString(input));
    }

    [Theory]
    [InlineData("Server=db;Password='Se;cr''et';Port=3306", "Server=db;Password=*****;Port=3306")]
    [InlineData("Server=db;Password=\"Se;cr\"\"et\";Port=3306", "Server=db;Password=*****;Port=3306")]
    public void MaskConnectionString_MasksQuotedValuesContainingSemicolons(string input, string expected)
    {
        Assert.Equal(expected, SensitiveDataMasker.MaskConnectionString(input));
    }

    [Fact]
    public void MaskConnectionString_MasksEveryPasswordKey()
    {
        var masked = SensitiveDataMasker.MaskConnectionString("Password=One;Server=db;SslKeyPassword=Two");

        Assert.DoesNotContain("One", masked);
        Assert.DoesNotContain("Two", masked);
    }

    [Theory]
    [InlineData("Server=db;Port=3306;User ID=root;Database=shop")]
    [InlineData("Access denied for user 'root'@'localhost' (using password: YES)")]
    [InlineData("The password is wrong.")]
    public void MaskText_LeavesTextWithoutAssignmentUnchanged(string input)
    {
        Assert.Equal(input, SensitiveDataMasker.MaskText(input));
    }

    [Fact]
    public void MaskText_MasksConnectionStringInsideMultiLineText()
    {
        var text = "Connect failed.\r\nConnection string: Server=db;Password=Secret123\r\n   at Foo.Bar()";

        var masked = SensitiveDataMasker.MaskText(text);

        Assert.DoesNotContain("Secret123", masked);
        Assert.Contains("Password=*****", masked);
        Assert.Contains("   at Foo.Bar()", masked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MaskText_ReturnsEmptyStringForNullOrEmpty(string? input)
    {
        Assert.Equal(string.Empty, SensitiveDataMasker.MaskText(input));
    }

    [Fact]
    public void MaskText_HandlesLongTextWithoutSecretsQuickly()
    {
        var text = new string('a', 100_000) + " " + new string('b', 100_000);

        Assert.Equal(text, SensitiveDataMasker.MaskText(text));
    }
}
