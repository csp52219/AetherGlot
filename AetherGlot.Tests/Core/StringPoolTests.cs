using AetherGlot.Core.Translation;
using Xunit;

namespace AetherGlot.Tests.Core;

public sealed class StringPoolTests
{
    [Fact]
    public void EqualStrings_ShareOneInstance()
    {
        var pool = new StringPool();
        var a = pool.Share(new string('x', 2000));
        var b = pool.Share(new string('x', 2000));
        Assert.Same(a, b);
        Assert.Equal(1, pool.Count);
    }

    [Fact]
    public void Pool_StaysBounded()
    {
        var pool = new StringPool(capacity: 3);
        for (var i = 0; i < 10; i++) pool.Share("prompt " + i);
        Assert.InRange(pool.Count, 1, 3);
    }
}
