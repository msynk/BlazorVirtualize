namespace BlazorVirtualize;

/// <summary>
/// A function that asynchronously supplies a window of items on demand.
/// </summary>
/// <typeparam name="TItem">The item type.</typeparam>
/// <param name="request">The window being requested.</param>
/// <returns>A task that resolves to the requested items and the total item count.</returns>
public delegate ValueTask<BlazorVirtualizeItemsProviderResult<TItem>> BlazorVirtualizeItemsProviderDelegate<TItem>(BlazorVirtualizeItemsProviderRequest request);
