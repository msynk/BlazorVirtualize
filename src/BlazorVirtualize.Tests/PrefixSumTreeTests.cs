using BlazorVirtualization.Internal;
using Xunit;

namespace BlazorVirtualize.Tests;

public class PrefixSumTreeTests
{
    [Fact]
    public void Defaults_ProduceUniformOffsets()
    {
        var tree = new BlazorVirtualizePrefixSumTree(10, 50);

        Assert.Equal(10, tree.Count);
        Assert.Equal(500, tree.Total);
        Assert.Equal(0, tree.PrefixSum(0));
        Assert.Equal(50, tree.PrefixSum(1));
        Assert.Equal(250, tree.PrefixSum(5));
        Assert.Equal(500, tree.PrefixSum(10));
    }

    [Fact]
    public void SetSize_UpdatesTotalAndPrefixSums()
    {
        var tree = new BlazorVirtualizePrefixSumTree(5, 50);

        tree.SetSize(0, 100);
        tree.SetSize(2, 200);

        Assert.Equal(50 * 3 + 100 + 200, tree.Total);
        Assert.Equal(0, tree.PrefixSum(0));
        Assert.Equal(100, tree.PrefixSum(1));   // [100]
        Assert.Equal(150, tree.PrefixSum(2));   // [100,50]
        Assert.Equal(350, tree.PrefixSum(3));   // [100,50,200]
        Assert.Equal(400, tree.PrefixSum(4));   // +50
        Assert.Equal(450, tree.PrefixSum(5));   // +50
    }

    [Fact]
    public void SetSize_ReturnsDelta()
    {
        var tree = new BlazorVirtualizePrefixSumTree(3, 50);
        Assert.Equal(30, tree.SetSize(1, 80));
        Assert.Equal(-30, tree.SetSize(1, 50));
        Assert.Equal(0, tree.SetSize(1, 50));
    }

    [Fact]
    public void FindIndex_FixedSizes_MapsOffsetToIndex()
    {
        var tree = new BlazorVirtualizePrefixSumTree(100, 50);

        Assert.Equal(0, tree.FindIndex(0));
        Assert.Equal(0, tree.FindIndex(49));
        Assert.Equal(1, tree.FindIndex(50));
        Assert.Equal(1, tree.FindIndex(99));
        Assert.Equal(2, tree.FindIndex(100));
        Assert.Equal(20, tree.FindIndex(1000));
        Assert.Equal(99, tree.FindIndex(100_000)); // clamps past the end
    }

    [Fact]
    public void FindIndex_VariableSizes_MapsOffsetToIndex()
    {
        var tree = new BlazorVirtualizePrefixSumTree(4, 0);
        tree.SetSize(0, 100); // [0,100)
        tree.SetSize(1, 200); // [100,300)
        tree.SetSize(2, 50);  // [300,350)
        tree.SetSize(3, 150); // [350,500)

        Assert.Equal(0, tree.FindIndex(0));
        Assert.Equal(0, tree.FindIndex(99));
        Assert.Equal(1, tree.FindIndex(100));
        Assert.Equal(1, tree.FindIndex(299));
        Assert.Equal(2, tree.FindIndex(300));
        Assert.Equal(2, tree.FindIndex(349));
        Assert.Equal(3, tree.FindIndex(350));
        Assert.Equal(3, tree.FindIndex(499));
    }

    [Fact]
    public void FindIndex_IsConsistentWithPrefixSum_ForRandomSizes()
    {
        var rnd = new Random(123);
        const int n = 2000;
        var tree = new BlazorVirtualizePrefixSumTree(n, 10);
        var sizes = new double[n];
        for (int i = 0; i < n; i++)
        {
            sizes[i] = rnd.Next(1, 100);
            tree.SetSize(i, sizes[i]);
        }

        // Brute-force prefix sums for verification.
        var prefix = new double[n + 1];
        for (int i = 0; i < n; i++)
        {
            prefix[i + 1] = prefix[i] + sizes[i];
        }

        for (int i = 0; i <= n; i++)
        {
            Assert.Equal(prefix[i], tree.PrefixSum(i), precision: 6);
        }

        // Every offset must resolve to the item whose half-open range contains it.
        for (int trial = 0; trial < 5000; trial++)
        {
            double offset = rnd.NextDouble() * prefix[n];
            int found = tree.FindIndex(offset);
            Assert.True(offset >= prefix[found] && offset < prefix[found + 1],
                $"offset {offset} resolved to index {found} with range [{prefix[found]}, {prefix[found + 1]})");
        }
    }

    [Fact]
    public void Reset_ReusesBufferAndReseeds()
    {
        var tree = new BlazorVirtualizePrefixSumTree(10, 50);
        tree.SetSize(3, 999);

        tree.Reset(6, 20);

        Assert.Equal(6, tree.Count);
        Assert.Equal(120, tree.Total);
        Assert.Equal(20, tree.GetSize(3));
        Assert.Equal(60, tree.PrefixSum(3));
    }
}
