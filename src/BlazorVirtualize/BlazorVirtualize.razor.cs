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

    /// <summary>
    /// Invoked when the last item comes within <see cref="ReachedThreshold"/> items of the visible window.
    /// Use this to append more data for infinite scrolling. Fires once per item-count value.
    /// </summary>
    [Parameter] public EventCallback OnEndReached { get; set; }

    /// <summary>
    /// Invoked when the first item comes within <see cref="ReachedThreshold"/> items of the visible window.
    /// Use this to prepend older data (for example, loading chat history when scrolling up).
    /// </summary>
    [Parameter] public EventCallback OnStartReached { get; set; }

    /// <summary>
    /// How many items away from an edge the visible window must be before
    /// <see cref="OnEndReached"/> / <see cref="OnStartReached"/> fire. Defaults to 0.
    /// </summary>
    [Parameter] public int ReachedThreshold { get; set; }

    /// <summary>
    /// When <c>true</c> the list is bottom-anchored: it starts scrolled to the end and
    /// automatically keeps the newest items in view when data is appended while the user
    /// is at the bottom. Ideal for chat and log views.
    /// </summary>
    [Parameter] public bool Reverse { get; set; }

    /// <summary>
    /// A predicate marking certain items (for example, group headers) as sticky. The active
    /// sticky item is pinned to the leading edge of the viewport while its group scrolls.
    /// Only supported with in-memory <see cref="Items"/>.
    /// </summary>
    [Parameter] public Func<TItem, bool>? IsStickyItem { get; set; }

    /// <summary>The template used to render a pinned sticky item. Falls back to <see cref="ItemContent"/> when not set.</summary>
    [Parameter] public RenderFragment<TItem>? StickyHeaderContent { get; set; }

    /// <summary>The index the list should be scrolled to on first render. Ignored when <see cref="Reverse"/> is set.</summary>
    [Parameter] public int? InitialIndex { get; set; }

    /// <summary>The ARIA role applied to the scroll viewport. Defaults to <c>list</c>.</summary>
    [Parameter] public string AriaRole { get; set; } = "list";

    /// <summary>An accessible label for the scroll viewport.</summary>
    [Parameter] public string? AriaLabel { get; set; }

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

    // Infinite-scroll edge tracking.
    private int _lastEndReachedCount = -1;
    private bool _wasAtStart;
    private bool _wasAtEnd;

    // Sticky (grouped) header tracking.
    private List<int>? _stickyIndices;   // sorted indices flagged by IsStickyItem (in-memory only)
    private int _stickyActiveIndex = -1;
    private double _stickyOffset;

    // Reverse / chat (bottom-anchored) tracking.
    private bool _pendingScrollToEnd;
    private double _preserveEndDistance = -1;
    private bool _initialScrollDone;
    private bool _stickToEnd;

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
            ComputeStickyIndices();
            RecomputeRange();
        }
        // Provider count is discovered on first load.
    }

    private void ComputeStickyIndices()
    {
        if (IsStickyItem is null || _itemList is null)
        {
            _stickyIndices = null;
            return;
        }
        var list = new List<int>();
        for (int i = 0; i < _itemList.Count; i++)
        {
            if (IsStickyItem(_itemList[i]))
            {
                list.Add(i);
            }
        }
        _stickyIndices = list;
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

            await ApplyInitialScrollAsync();
        }
        else
        {
            if (Dynamic && _module is not null && _itemCount > 0)
            {
                // Measure any newly rendered items and reconcile observer subscriptions.
                await _module.InvokeVoidAsync("syncMeasurements", _jsId);
            }

            await ApplyPendingScrollAsync();
        }
    }

    private async Task ApplyInitialScrollAsync()
    {
        if (_initialScrollDone || _module is null || _itemCount == 0)
        {
            return;
        }
        _initialScrollDone = true;

        if (Reverse)
        {
            _stickToEnd = true;
            await ScrollToEndAsync();
        }
        else if (InitialIndex is { } idx)
        {
            await ScrollToIndexAsync(idx);
        }
    }

    private async Task ApplyPendingScrollAsync()
    {
        if (_module is null)
        {
            return;
        }

        if (_pendingScrollToEnd)
        {
            _pendingScrollToEnd = false;
            await ScrollToEndAsync();
        }
        else if (_preserveEndDistance >= 0)
        {
            // Restore the distance from the end after a prepend so the viewport stays put.
            double target = Math.Max(0, GetTotalSize() - _viewportSize - _preserveEndDistance);
            _preserveEndDistance = -1;
            await _module.InvokeVoidAsync("scrollToOffset", _jsId, target, false);
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
            bool atEnd = Reverse && (_stickToEnd || IsNearEnd());
            double prevTotal = GetTotalSize();

            _itemList = Items as IList<TItem> ?? Items.ToList();
            SetItemCount(_itemList.Count);
            ComputeStickyIndices();
            RecomputeRange();

            if (Reverse && GetTotalSize() > prevTotal)
            {
                if (atEnd)
                {
                    // User was at the bottom: keep newest items in view.
                    _pendingScrollToEnd = true;
                }
                else
                {
                    // Content grew (likely prepended history): keep the viewport anchored
                    // to the same distance from the end so it does not jump.
                    _preserveEndDistance = prevTotal - (_scrollOffset + _viewportSize);
                }
            }

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
            _stickyActiveIndex = -1;
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

        UpdateSticky();
        CheckEdgesReached();

        if (rangeChanged)
        {
            _ = NotifyRangeChangedAsync();
        }
    }

    private void UpdateSticky()
    {
        if (_stickyIndices is null || _stickyIndices.Count == 0)
        {
            _stickyActiveIndex = -1;
            return;
        }

        // Greatest sticky index that starts at or before the first visible item.
        int active = -1;
        int lo = 0, hi = _stickyIndices.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (_stickyIndices[mid] <= _visibleStart)
            {
                active = _stickyIndices[mid];
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        _stickyActiveIndex = active;
        if (active < 0)
        {
            return;
        }

        double pinned = _scrollOffset;
        double headerSize = GetItemSize(active);

        // The next sticky header pushes the current one out as it approaches.
        int l = 0, h = _stickyIndices.Count - 1, next = -1;
        while (l <= h)
        {
            int mid = (l + h) >> 1;
            if (_stickyIndices[mid] > active)
            {
                next = _stickyIndices[mid];
                h = mid - 1;
            }
            else
            {
                l = mid + 1;
            }
        }
        if (next >= 0)
        {
            double nextOffset = GetItemOffset(next);
            if (nextOffset < pinned + headerSize)
            {
                pinned -= pinned + headerSize - nextOffset;
            }
        }

        _stickyOffset = pinned;
    }

    private bool IsNearEnd() => GetTotalSize() - (_scrollOffset + _viewportSize) <= 4d;

    private void CheckEdgesReached()
    {
        if (_itemCount == 0)
        {
            return;
        }

        bool atEnd = _visibleEnd >= _itemCount - ReachedThreshold;
        if (OnEndReached.HasDelegate && atEnd && (!_wasAtEnd || _lastEndReachedCount != _itemCount))
        {
            _lastEndReachedCount = _itemCount;
            _ = OnEndReached.InvokeAsync();
        }
        _wasAtEnd = atEnd;

        bool atStart = _visibleStart <= ReachedThreshold;
        if (OnStartReached.HasDelegate && atStart && !_wasAtStart && _initialScrollDone)
        {
            _ = OnStartReached.InvokeAsync();
        }
        _wasAtStart = atStart;
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

        if (Reverse)
        {
            _stickToEnd = IsNearEnd();
        }

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

        // In reverse (chat) mode, keep the newest items pinned as measurements settle.
        if (Reverse && _stickToEnd && _module is not null)
        {
            await ScrollToEndAsync();
        }
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

    /// <summary>Scrolls to an absolute pixel offset along the scroll axis.</summary>
    public async Task ScrollToOffsetAsync(double offset, bool smooth = false)
    {
        if (_module is null)
        {
            return;
        }
        double maxOffset = Math.Max(0, GetTotalSize() - _viewportSize);
        await _module.InvokeVoidAsync("scrollToOffset", _jsId, Math.Clamp(offset, 0, maxOffset), smooth);
    }

    /// <summary>Scrolls to the start (top/left) of the list.</summary>
    public Task ScrollToStartAsync(bool smooth = false) => ScrollToOffsetAsync(0, smooth);

    /// <summary>Scrolls to the end (bottom/right) of the list. Useful for chat and log views.</summary>
    public Task ScrollToEndAsync(bool smooth = false) => ScrollToOffsetAsync(GetTotalSize(), smooth);

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
