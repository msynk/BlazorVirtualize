namespace BlazorVirtualize;

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
