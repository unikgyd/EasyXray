using XrayDeploy.Core;

namespace XrayDeploy.Tests;

public sealed class PosixShellEscaperTests
{
    [Fact]
    public void QuotesSingleQuotesWithoutLeavingShellSyntaxOpen()
    {
        Assert.Equal("'a'\"'\"'b'", PosixShellEscaper.Quote("a'b"));
    }
}
