namespace BlazorVirtualize;

/// <summary>
/// A request to an <see cref="BlazorVirtualizeItemsProviderDelegate{TItem}"/> for a window of items.
/// </summary>
public readonly struct BlazorVirtualizeItemsProviderRequest
{
    /// <summary>
    /// Creates a new <see cref="BlazorVirtualizeItemsProviderRequest"/>.
    /// </summary>
    /// <param name="startIndex">The (inclusive) zero-based index of the first item requested.</param>
    /// <param name="count">The maximum number of items requested.</param>
    /// <param name="cancellationToken">A token that is cancelled when the request is superseded.</param>
    public BlazorVirtualizeItemsProviderRequest(int startIndex, int count, CancellationToken cancellationToken)
    {
        StartIndex = startIndex;
        Count = count;
        CancellationToken = cancellationToken;
    }

    /// <summary>The zero-based index of the first item requested.</summary>
    public int StartIndex { get; }

    /// <summary>The maximum number of items requested.</summary>
    public int Count { get; }

    /// <summary>A token that is cancelled when this request is no longer needed.</summary>
    public CancellationToken CancellationToken { get; }
}

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

/// <summary>
/// A function that asynchronously supplies a window of items on demand.
/// </summary>
/// <typeparam name="TItem">The item type.</typeparam>
/// <param name="request">The window being requested.</param>
/// <returns>A task that resolves to the requested items and the total item count.</returns>
public delegate ValueTask<BlazorVirtualizeItemsProviderResult<TItem>> BlazorVirtualizeItemsProviderDelegate<TItem>(BlazorVirtualizeItemsProviderRequest request);

/// <summary>
/// Context passed to the placeholder template while real items are being loaded.
/// </summary>
public readonly struct BlazorVirtualizePlaceholderContext
{
    /// <summary>
    /// Creates a new <see cref="BlazorVirtualizePlaceholderContext"/>.
    /// </summary>
    public BlazorVirtualizePlaceholderContext(int index, double size)
    {
        Index = index;
        Size = size;
    }

    /// <summary>The zero-based index of the item this placeholder represents.</summary>
    public int Index { get; }

    /// <summary>The estimated size (px) reserved for the placeholder along the scroll axis.</summary>
    public double Size { get; }
}
