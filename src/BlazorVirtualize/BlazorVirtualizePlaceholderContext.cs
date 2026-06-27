namespace BlazorVirtualize;

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
