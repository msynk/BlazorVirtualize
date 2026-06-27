using BlazorVirtualize.Internal;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlazorVirtualize;

/// <summary>
/// A high-performance virtualization (windowing) component that renders only the items
/// currently visible in its scroll viewport (plus a configurable overscan buffer).
/// <para>
/// Supports fixed and dynamically measured item sizes, vertical and horizontal
/// orientation, in-memory <see cref="Items"/> or lazy <see cref="ItemsProvider"/> data,
/// placeholders, and scroll anchoring that prevents content from jumping as dynamic
/// items are measured.
/// </para>
/// </summary>
/// <typeparam name="TItem">The type of item rendered by the list.</typeparam>
public sealed partial class BlazorVirtualize<TItem> : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ---- Parameters --------------------------------------------------------

    /// <summary>The template used to render each item.</summary>
    [Parameter, EditorRequired] public RenderFragment<TItem> ItemContent { get; set; } = default!;

    /// <summary>An in-memory collection of items to virtualize. Mutually exclusive with <see cref="ItemsProvider"/>.</summary>
    [Parameter] public ICollection<TItem>? Items { get; set; }

    /// <summary>A callback that lazily supplies windows of items on demand. Mutually exclusive with <see cref="Items"/>.</summary>
    [Parameter] public BlazorVirtualizeItemsProviderDelegate<TItem>? ItemsProvider { get; set; }

    /// <summary>Template rendered for an item whose data has not yet loaded (provider mode).</summary>
    [Parameter] public RenderFragment<BlazorVirtualizePlaceholderContext>? Placeholder { get; set; }

    /// <summary>Content rendered when there are no items.</summary>
    [Parameter] public RenderFragment? EmptyContent { get; set; }

    /// <summary>Content rendered before the component has performed its first load.</summary>
    [Parameter] public RenderFragment? LoadingContent { get; set; }

    /// <summary>How item sizes are determined. Defaults to <see cref="BlazorVirtualizeSizeMode.Fixed"/>.</summary>
    [Parameter] public BlazorVirtualizeSizeMode SizeMode { get; set; } = BlazorVirtualizeSizeMode.Fixed;

    /// <summary>The size in pixels of each item along the scroll axis when using <see cref="BlazorVirtualizeSizeMode.Fixed"/>.</summary>
    [Parameter] public float ItemSize { get; set; } = 50f;

    /// <summary>The assumed size in pixels of items that have not yet been measured in <see cref="BlazorVirtualizeSizeMode.Dynamic"/> mode.</summary>
    [Parameter] public float EstimatedItemSize { get; set; } = 50f;

    /// <summary>The scroll axis. Defaults to <see cref="BlazorVirtualizeOrientation.Vertical"/>.</summary>
    [Parameter] public BlazorVirtualizeOrientation Orientation { get; set; } = BlazorVirtualizeOrientation.Vertical;

    /// <summary>The number of extra items to render on each side of the visible window for smoother scrolling.</summary>
    [Parameter] public int OverscanCount { get; set; } = 3;

    /// <summary>Invoked whenever the rendered (visible) index range changes.</summary>
    [Parameter] public EventCallback<(int Start, int End)> OnVisibleRangeChanged { get; set; }

    /// <summary>Additional attributes (such as <c>style</c> or <c>class</c>) applied to the scroll viewport element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>The viewport element's CSS class, merging the component's own classes with any user-supplied <c>class</c>.</summary>
    private string CssClass
    {
        get
        {
            var baseClass = Horizontal ? "bv-viewport bv-horizontal" : "bv-viewport bv-vertical";
            if (AdditionalAttributes is not null
                && AdditionalAttributes.TryGetValue("class", out var value)
                && value is string s && !string.IsNullOrWhiteSpace(s))
            {
                return $"{baseClass} {s}";
            }
            return baseClass;
        }
    }

    /// <summary>The unmatched attributes to splat, excluding <c>class</c> (handled by <see cref="CssClass"/>).</summary>
    private IReadOnlyDictionary<string, object>? SplatAttributes
    {
        get
        {
            if (AdditionalAttributes is null || !AdditionalAttributes.ContainsKey("class"))
            {
                return AdditionalAttributes;
            }
            var copy = new Dictionary<string, object>(AdditionalAttributes.Count);
            foreach (var kv in AdditionalAttributes)
            {
                if (kv.Key != "class")
                {
                    copy[kv.Key] = kv.Value;
                }
            }
            return copy;
        }
    }

    // ---- State -------------------------------------------------------------

    private ElementReference _viewport;
    private IJSObjectReference? _module;
    private DotNetObjectReference<BlazorVirtualize<TItem>>? _selfRef;
    private int _jsId;

    private IList<TItem>? _itemList;          // materialised view of Items
    private IReadOnlyList<TItem>? _loadedItems; // current provider window
    private int _loadedStart;
    private int _itemCount;
    private bool _initialized;
    private bool _loading;

    private BlazorVirtualizePrefixSumTree? _tree;             // dynamic mode only
    private double _scrollOffset;
    private double _viewportSize;
    private int _visibleStart;
    private int _visibleEnd;   // exclusive
    private int _renderStart;
    private int _renderEnd;    // exclusive

    private CancellationTokenSource? _loadCts;
    private bool _refreshPending;

    private bool Horizontal => Orientation == BlazorVirtualizeOrientation.Horizontal;
    private bool Dynamic => SizeMode == BlazorVirtualizeSizeMode.Dynamic;

    // ---- Lifecycle ---------------------------------------------------------

    protected override void OnParametersSet()
    {
        if (Items is not null && ItemsProvider is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(BlazorVirtualize<TItem>)} requires either {nameof(Items)} or {nameof(ItemsProvider)}, but not both.");
        }
        if (ItemContent is null)
        {
            throw new InvalidOperationException($"{nameof(BlazorVirtualize<TItem>)} requires {nameof(ItemContent)} to be set.");
        }

        if (Items is not null)
        {
            _itemList = Items as IList<TItem> ?? Items.ToList();
            SetItemCount(_itemList.Count);
            _loadedItems = null;
            RecomputeRange();
        }
        // Provider count is discovered on first load.
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _module = await JS.InvokeAsync<IJSObjectReference>(
                "import", "./_content/BlazorVirtualize/virtualize.js");
            _selfRef = DotNetObjectReference.Create(this);

            var result = await _module.InvokeAsync<BlazorVirtualizeInitResult>(
                "init", _viewport, _selfRef, new { horizontal = Horizontal });

            _jsId = result.Id;
            _viewportSize = result.ViewportSize;
            _scrollOffset = result.ScrollOffset;
            _initialized = true;

            await InitialLoadAsync();

            if (_refreshPending)
            {
                _refreshPending = false;
                await RefreshDataAsync();
            }
        }
        else if (Dynamic && _module is not null && _itemCount > 0)
        {
            // Measure any newly rendered items and reconcile observer subscriptions.
            await _module.InvokeVoidAsync("syncMeasurements", _jsId);
        }
    }

    // ---- Data --------------------------------------------------------------

    private void SetItemCount(int count)
    {
        if (count == _itemCount && _tree is not null == Dynamic)
        {
            return;
        }
        _itemCount = count;
        if (Dynamic)
        {
            _tree ??= new BlazorVirtualizePrefixSumTree(count, EstimatedItemSize);
            _tree.Reset(count, EstimatedItemSize);
        }
        else
        {
            _tree = null;
        }
    }

    private async Task InitialLoadAsync()
    {
        RecomputeRange();
        if (ItemsProvider is not null)
        {
            await LoadProviderWindowAsync(forceCount: true);
        }
        StateHasChanged();
    }

    /// <summary>Re-requests data from the <see cref="ItemsProvider"/> (or re-reads <see cref="Items"/>) and refreshes the view.</summary>
    public async Task RefreshDataAsync()
    {
        if (!_initialized)
        {
            _refreshPending = true;
            return;
        }

        if (Items is not null)
        {
            _itemList = Items as IList<TItem> ?? Items.ToList();
            SetItemCount(_itemList.Count);
            RecomputeRange();
            StateHasChanged();
        }
        else if (ItemsProvider is not null)
        {
            await LoadProviderWindowAsync(forceCount: true);
        }
    }

    private async Task LoadProviderWindowAsync(bool forceCount)
    {
        if (ItemsProvider is null)
        {
            return;
        }

        int start = Math.Max(0, _renderStart);
        // When the count is still unknown, fetch a screen-sized window from the top.
        int count = _initialized && _itemCount > 0
            ? Math.Max(1, _renderEnd - start)
            : Math.Max(1, EstimateInitialCount());

        // Skip the load if the requested window is already fully cached.
        if (!forceCount && _loadedItems is not null &&
            start >= _loadedStart && _renderEnd <= _loadedStart + _loadedItems.Count)
        {
            return;
        }

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        _loading = _loadedItems is null;
        try
        {
            var result = await ItemsProvider(new BlazorVirtualizeItemsProviderRequest(start, count, token));
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (result.TotalItemCount != _itemCount)
            {
                SetItemCount(result.TotalItemCount);
            }

            _loadedItems = result.Items;
            _loadedStart = start;
            _loading = false;

            RecomputeRange();
            StateHasChanged();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request; ignore.
        }
    }

    private int EstimateInitialCount()
    {
        double size = Dynamic ? EstimatedItemSize : ItemSize;
        double viewport = _viewportSize > 0 ? _viewportSize : 600;
        return (int)Math.Ceiling(viewport / Math.Max(1, size)) + (OverscanCount * 2) + 1;
    }

    private bool TryGetItem(int index, out TItem item)
    {
        if (_itemList is not null)
        {
            item = _itemList[index];
            return true;
        }
        if (_loadedItems is not null)
        {
            int local = index - _loadedStart;
            if (local >= 0 && local < _loadedItems.Count)
            {
                item = _loadedItems[local];
                return true;
            }
        }
        item = default!;
        return false;
    }

    // ---- Geometry ----------------------------------------------------------

    private double GetItemOffset(int index) =>
        Dynamic ? _tree!.PrefixSum(index) : index * (double)ItemSize;

    private double GetItemSize(int index) =>
        Dynamic ? _tree!.GetSize(index) : ItemSize;

    private double GetTotalSize() =>
        Dynamic ? _tree!.Total : _itemCount * (double)ItemSize;

    private int FindIndexAtOffset(double offset)
    {
        if (_itemCount == 0)
        {
            return 0;
        }
        if (Dynamic)
        {
            return _tree!.FindIndex(offset);
        }
        int index = (int)Math.Floor(offset / Math.Max(1, ItemSize));
        return Math.Clamp(index, 0, _itemCount - 1);
    }

    private void RecomputeRange()
    {
        if (_itemCount == 0 || _viewportSize <= 0)
        {
            int prevStart = _visibleStart, prevEnd = _visibleEnd;
            _visibleStart = _renderStart = 0;
            _visibleEnd = _renderEnd = Math.Min(_itemCount, _initialized ? 0 : EstimateInitialCount());
            if (prevStart != _visibleStart || prevEnd != _visibleEnd)
            {
                _ = NotifyRangeChangedAsync();
            }
            return;
        }

        double maxOffset = Math.Max(0, GetTotalSize() - _viewportSize);
        double offset = Math.Clamp(_scrollOffset, 0, maxOffset);

        int start = FindIndexAtOffset(offset);
        int end = FindIndexAtOffset(offset + _viewportSize) + 1;
        end = Math.Min(end, _itemCount);

        int newVisibleStart = start;
        int newVisibleEnd = end;
        int newRenderStart = Math.Max(0, start - OverscanCount);
        int newRenderEnd = Math.Min(_itemCount, end + OverscanCount);

        bool rangeChanged = newVisibleStart != _visibleStart || newVisibleEnd != _visibleEnd;

        _visibleStart = newVisibleStart;
        _visibleEnd = newVisibleEnd;
        _renderStart = newRenderStart;
        _renderEnd = newRenderEnd;

        if (rangeChanged)
        {
            _ = NotifyRangeChangedAsync();
        }
    }

    private Task NotifyRangeChangedAsync() =>
        OnVisibleRangeChanged.HasDelegate
            ? OnVisibleRangeChanged.InvokeAsync((_visibleStart, _visibleEnd)).ContinueWith(_ => { })
            : Task.CompletedTask;

    // ---- JS callbacks ------------------------------------------------------

    /// <summary>Invoked from JavaScript when the viewport is scrolled or resized.</summary>
    [JSInvokable]
    public async Task OnScroll(double scrollOffset, double viewportSize)
    {
        _scrollOffset = scrollOffset;
        _viewportSize = viewportSize;

        int prevRenderStart = _renderStart, prevRenderEnd = _renderEnd;
        RecomputeRange();

        if (_renderStart != prevRenderStart || _renderEnd != prevRenderEnd)
        {
            if (ItemsProvider is not null)
            {
                await LoadProviderWindowAsync(forceCount: false);
            }
            StateHasChanged();
        }
    }

    /// <summary>Invoked from JavaScript with measured sizes for currently rendered items (dynamic mode).</summary>
    [JSInvokable]
    public async Task OnItemsMeasured(int[] indices, double[] sizes)
    {
        if (!Dynamic || _tree is null || indices.Length == 0)
        {
            return;
        }

        int anchor = _visibleStart;
        double oldAnchorOffset = _tree.PrefixSum(anchor);

        bool changed = false;
        for (int i = 0; i < indices.Length; i++)
        {
            int idx = indices[i];
            if (idx < 0 || idx >= _itemCount)
            {
                continue;
            }
            if (_tree.SetSize(idx, sizes[i]) != 0d)
            {
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        // Scroll anchoring: keep the first visible item visually stable when the
        // cumulative size of the items above it changes.
        double newAnchorOffset = _tree.PrefixSum(anchor);
        double diff = newAnchorOffset - oldAnchorOffset;
        if (Math.Abs(diff) > 0.01 && _module is not null)
        {
            _scrollOffset += diff;
            await _module.InvokeVoidAsync("adjustScroll", _jsId, diff);
        }

        RecomputeRange();
        StateHasChanged();
    }

    // ---- Public API --------------------------------------------------------

    /// <summary>Scrolls the viewport so that the item at <paramref name="index"/> is visible.</summary>
    /// <param name="index">The zero-based index of the target item.</param>
    /// <param name="alignment">Where the item should be positioned within the viewport.</param>
    /// <param name="smooth">Whether to animate the scroll.</param>
    public async Task ScrollToIndexAsync(int index, BlazorVirtualizeScrollAlignment alignment = BlazorVirtualizeScrollAlignment.Start, bool smooth = false)
    {
        if (_module is null || _itemCount == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, _itemCount - 1);
        double offset = GetItemOffset(index);
        double size = GetItemSize(index);
        double target = alignment switch
        {
            BlazorVirtualizeScrollAlignment.Start => offset,
            BlazorVirtualizeScrollAlignment.Center => offset - (_viewportSize - size) / 2d,
            BlazorVirtualizeScrollAlignment.End => offset - (_viewportSize - size),
            _ => ResolveAutoAlignment(offset, size)
        };

        double maxOffset = Math.Max(0, GetTotalSize() - _viewportSize);
        target = Math.Clamp(target, 0, maxOffset);
        await _module.InvokeVoidAsync("scrollToOffset", _jsId, target, smooth);
    }

    private double ResolveAutoAlignment(double offset, double size)
    {
        if (offset < _scrollOffset)
        {
            return offset; // above the viewport -> align to start
        }
        if (offset + size > _scrollOffset + _viewportSize)
        {
            return offset - (_viewportSize - size); // below the viewport -> align to end
        }
        return _scrollOffset; // already visible -> no change
    }

    // ---- Cleanup -----------------------------------------------------------

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();

        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("dispose", _jsId);
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException) { /* circuit already gone */ }
            catch (OperationCanceledException) { }
        }

        _selfRef?.Dispose();
    }

    private sealed class BlazorVirtualizeInitResult
    {
        public int Id { get; set; }
        public double ScrollOffset { get; set; }
        public double ViewportSize { get; set; }
    }
}
