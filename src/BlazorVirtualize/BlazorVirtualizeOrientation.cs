namespace BlazorVirtualize;

/// <summary>
/// The scroll axis along which the list is virtualized.
/// </summary>
public enum BlazorVirtualizeOrientation
{
    /// <summary>Items are stacked top to bottom and the viewport scrolls vertically.</summary>
    Vertical,

    /// <summary>Items are laid out left to right and the viewport scrolls horizontally.</summary>
    Horizontal
}
