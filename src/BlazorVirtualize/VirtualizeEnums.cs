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

/// <summary>
/// Controls how item sizes (height for vertical, width for horizontal) are determined.
/// </summary>
public enum BlazorVirtualizeSizeMode
{
    /// <summary>
    /// Every item uses the same, known size supplied via <c>ItemSize</c>.
    /// This is the fastest mode and requires no measurement.
    /// </summary>
    Fixed,

    /// <summary>
    /// Items may have different sizes. Each rendered item is measured in the browser
    /// and its real size is cached, with unmeasured items using an estimated size.
    /// </summary>
    Dynamic
}

/// <summary>
/// Determines where a target item is positioned within the viewport when scrolling to it.
/// </summary>
public enum BlazorVirtualizeScrollAlignment
{
    /// <summary>Scroll the minimum amount required to bring the item fully into view.</summary>
    Auto,

    /// <summary>Align the item to the start (top/left) of the viewport.</summary>
    Start,

    /// <summary>Center the item within the viewport.</summary>
    Center,

    /// <summary>Align the item to the end (bottom/right) of the viewport.</summary>
    End
}
