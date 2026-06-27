# BlazorVirtualize

A full-featured, **native** virtualization (windowing) component for Blazor. It renders
only the items currently visible in the scroll viewport - plus a small overscan buffer -
so a list of one million rows keeps the same constant number of DOM nodes as a list of ten.

Unlike the built-in `<Virtualize>`, this component supports **dynamically measured item
sizes** with **scroll anchoring**, **horizontal** orientation, and exposes a small
imperative API (`ScrollToIndexAsync`, `RefreshDataAsync`).

> **Naming:** the component type is `BlazorVirtualize`. To avoid a type-vs-namespace
> collision, the library's code lives in the **`BlazorVirtualization`** namespace, while
> the assembly/package (and therefore the static-asset path `_content/BlazorVirtualize/`)
> stays `BlazorVirtualize`. Every public type is prefixed with `BlazorVirtualize`.

## Features

| Capability | Built-in `<Virtualize>` | `<BlazorVirtualize>` |
|---|---|---|
| Fixed item size | ✅ | ✅ |
| Dynamic / variable item size (real measurement) | ❌ | ✅ |
| Scroll anchoring (no jump as items measure) | ❌ | ✅ |
| Vertical orientation | ✅ | ✅ |
| Horizontal orientation | ❌ | ✅ |
| In-memory `Items` | ✅ | ✅ |
| Lazy `ItemsProvider` | ✅ | ✅ |
| Placeholder / empty / loading templates | partial | ✅ |
| `ScrollToIndex` API with alignment | ❌ | ✅ |
| Overscan buffer | ❌ | ✅ |

## How it works

The engine is split between C# (state + layout math) and a small JS module (DOM
observation + scrolling).

1. **Windowing.** A spacer element is sized to the full scrollable extent so the native
   scrollbar behaves normally. Each rendered item is absolutely positioned with
   `transform: translateY/translateX(offset)`.
2. **Fixed mode** computes offsets and the visible range with O(1) arithmetic
   (`index = floor(scrollOffset / itemSize)`).
3. **Dynamic mode** keeps every item size in a **Fenwick (binary indexed) tree**
   (`BlazorVirtualizePrefixSumTree`). This answers two questions per scroll frame in
   **O(log n)**: the cumulative offset of an item (`PrefixSum`) and the item occupying a
   given scroll offset (`FindIndex`, via a Fenwick binary search). Sizes update in
   O(log n) as items are measured.
4. **Measurement.** A `ResizeObserver` in the browser measures each rendered item and
   reports real sizes back to .NET in batches (one flush per animation frame).
5. **Scroll anchoring.** When items *above* the viewport are measured and their cumulative
   size changes, the component adjusts `scrollTop` by exactly that delta so the content the
   user is looking at never jumps. The browser's own `overflow-anchor` is disabled so the
   component stays in full control.
6. **Scroll/resize** are observed passively and throttled to a single notification per
   `requestAnimationFrame`.

## Installation

Reference the `BlazorVirtualize` project (or package), then import the namespace. The JS and
CSS assets are bundled as static web assets - no manual `<script>`/`<link>` tags required
(scoped CSS is included via `<App>.styles.css`, and the JS module is imported on demand).

```razor
@using BlazorVirtualization
```

## Usage

### Fixed size

```razor
<BlazorVirtualize TItem="int" Items="items" ItemSize="40" style="height:500px;">
    <ItemContent>
        <div>Row #@context</div>
    </ItemContent>
</BlazorVirtualize>
```

### Dynamic / variable size

```razor
<BlazorVirtualize TItem="Post" Items="posts"
                  SizeMode="BlazorVirtualizeSizeMode.Dynamic" EstimatedItemSize="80"
                  style="height:520px;">
    <ItemContent>
        <div class="post">
            <h3>@context.Title</h3>
            <p>@context.Body</p>
        </div>
    </ItemContent>
</BlazorVirtualize>
```

`EstimatedItemSize` is used for rows that have not been measured yet; once a row is
rendered it is measured and the layout corrects itself without a visible jump.

### Lazy loading with `ItemsProvider`

```razor
<BlazorVirtualize TItem="string" ItemsProvider="LoadItems" ItemSize="44" style="height:500px;">
    <ItemContent>
        <div>@context</div>
    </ItemContent>
    <Placeholder>
        <div class="placeholder">Loading item @context.Index…</div>
    </Placeholder>
</BlazorVirtualize>

@code {
    async ValueTask<BlazorVirtualizeItemsProviderResult<string>> LoadItems(BlazorVirtualizeItemsProviderRequest request)
    {
        var (data, total) = await myService.GetPageAsync(request.StartIndex, request.Count, request.CancellationToken);
        return new BlazorVirtualizeItemsProviderResult<string>(data, total);
    }
}
```

### Horizontal

```razor
<BlazorVirtualize TItem="int" Items="items" ItemSize="160"
                  Orientation="BlazorVirtualizeOrientation.Horizontal" style="height:200px;">
    <ItemContent>
        <div class="card">@context</div>
    </ItemContent>
</BlazorVirtualize>
```

### Imperative API

```razor
<BlazorVirtualize @ref="list" TItem="int" Items="items" ItemSize="40" style="height:500px;" />

@code {
    BlazorVirtualize<int>? list;

    Task GoTo(int i) => list!.ScrollToIndexAsync(i, BlazorVirtualizeScrollAlignment.Center, smooth: true);
    Task Reload()   => list!.RefreshDataAsync();
}
```

## Parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `ItemContent` *(required)* | `RenderFragment<TItem>` | - | Template for each item. |
| `Items` | `ICollection<TItem>?` | - | In-memory data source. |
| `ItemsProvider` | `BlazorVirtualizeItemsProviderDelegate<TItem>?` | - | Lazy data source. Mutually exclusive with `Items`. |
| `Placeholder` | `RenderFragment<BlazorVirtualizePlaceholderContext>?` | - | Shown while a provider item loads. |
| `EmptyContent` | `RenderFragment?` | - | Shown when there are no items. |
| `LoadingContent` | `RenderFragment?` | - | Shown before the first load completes. |
| `SizeMode` | `BlazorVirtualizeSizeMode` | `Fixed` | `Fixed` or `Dynamic`. |
| `ItemSize` | `float` | `50` | Item size (px) in `Fixed` mode. |
| `EstimatedItemSize` | `float` | `50` | Assumed size (px) for unmeasured items in `Dynamic` mode. |
| `Orientation` | `BlazorVirtualizeOrientation` | `Vertical` | `Vertical` or `Horizontal`. |
| `OverscanCount` | `int` | `3` | Extra items rendered on each side of the viewport. |
| `OnVisibleRangeChanged` | `EventCallback<(int,int)>` | - | Raised when the visible index range changes. |
| *(unmatched)* | attributes | - | Forwarded to the scroll viewport (e.g. `style`, `class`). |

> The viewport needs an explicit size along the scroll axis (e.g. `style="height:500px"`
> for vertical). The component sets `overflow:auto` itself.

## Public types

All public types are prefixed with `BlazorVirtualize` and live in the `BlazorVirtualization` namespace:

- `BlazorVirtualize<TItem>` - the component.
- `BlazorVirtualizeSizeMode`, `BlazorVirtualizeOrientation`, `BlazorVirtualizeScrollAlignment` - enums.
- `BlazorVirtualizeItemsProviderDelegate<TItem>`, `BlazorVirtualizeItemsProviderRequest`, `BlazorVirtualizeItemsProviderResult<TItem>` - lazy-loading API.
- `BlazorVirtualizePlaceholderContext` - placeholder template context.

## Project layout

```
src/BlazorVirtualize/                  The component library (Razor Class Library)
  BlazorVirtualize.razor(.cs/.css)     Component markup, logic, scoped styles
  Internal/PrefixSumTree.cs            Fenwick tree (BlazorVirtualizePrefixSumTree)
  wwwroot/virtualize.js                Scroll/measure/scroll-anchor engine
src/BlazorVirtualize.Sample            Runnable demos (fixed, dynamic, provider, horizontal)
src/BlazorVirtualize.Tests             Unit tests for the core algorithm
```

## Build & run

```bash
dotnet build
dotnet test
dotnet run --project src/BlazorVirtualize.Sample
```
