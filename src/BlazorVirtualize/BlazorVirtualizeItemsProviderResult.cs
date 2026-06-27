namespace BlazorVirtualize;

/// <summary>
/// The result returned from an <see cref="BlazorVirtualizeItemsProviderDelegate{TItem}"/>.
/// </summary>
/// <typeparam name="TItem">The item type.</typeparam>
public readonly struct BlazorVirtualizeItemsProviderResult<TItem>
{
    /// <summary>
    /// Creates a new <see cref="BlazorVirtualizeItemsProviderResult{TItem}"/>.
    /// </summary>
    /// <param name="items">The items for the requested window.</param>
    /// <param name="totalItemCount">The total number of items in the underlying data source.</param>
    public BlazorVirtualizeItemsProviderResult(IReadOnlyList<TItem> items, int totalItemCount)
    {
        Items = items;
        TotalItemCount = totalItemCount;
    }

    /// <summary>The items that were loaded for the requested window.</summary>
    public IReadOnlyList<TItem> Items { get; }

    /// <summary>The total number of items in the underlying data source.</summary>
    public int TotalItemCount { get; }
}
