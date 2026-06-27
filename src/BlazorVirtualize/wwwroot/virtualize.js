// BlazorVirtualize - browser-side engine.
//
// Responsibilities:
//   * Observe the scroll position and viewport size of the scroll container and
//     report changes back to .NET (throttled to one notification per animation frame).
//   * Measure rendered items with a ResizeObserver (dynamic-size mode) and report
//     real sizes back to .NET in batches.
//   * Provide programmatic scrolling and scroll-anchor correction so dynamic
//     measurements never make the content visibly jump.
//
// One Instance object is created per <Virtualize> component.

const instances = new Map();
let nextId = 0;

class Instance {
    constructor(element, dotNet, options) {
        this.element = element;
        this.dotNet = dotNet;
        this.horizontal = options.horizontal === true;
        this.scrollScheduled = false;
        this.disposed = false;

        // index -> element, tracks which items are currently observed.
        this.observed = new Map();
        this.pendingMeasure = new Map(); // index -> size, batched until next frame
        this.measureScheduled = false;
        // Set while we programmatically adjust scroll, to suppress the resulting
        // scroll event from being treated as a user scroll.
        this.suppressScroll = false;

        this.onScroll = this.onScroll.bind(this);
        this.flushScroll = this.flushScroll.bind(this);
        this.flushMeasure = this.flushMeasure.bind(this);

        this.element.addEventListener('scroll', this.onScroll, { passive: true });

        // Track viewport resizes.
        this.viewportObserver = new ResizeObserver(() => this.onScroll());
        this.viewportObserver.observe(this.element);

        // Track item resizes (dynamic mode).
        this.itemObserver = new ResizeObserver((entries) => this.onItemsResized(entries));
    }

    metrics() {
        return this.horizontal
            ? { scrollOffset: this.element.scrollLeft, viewportSize: this.element.clientWidth }
            : { scrollOffset: this.element.scrollTop, viewportSize: this.element.clientHeight };
    }

    onScroll() {
        if (this.disposed || this.suppressScroll) {
            return;
        }
        if (!this.scrollScheduled) {
            this.scrollScheduled = true;
            requestAnimationFrame(this.flushScroll);
        }
    }

    flushScroll() {
        this.scrollScheduled = false;
        if (this.disposed) {
            return;
        }
        const m = this.metrics();
        this.dotNet.invokeMethodAsync('OnScroll', m.scrollOffset, m.viewportSize);
    }

    measureElement(el) {
        const rect = el.getBoundingClientRect();
        return this.horizontal ? rect.width : rect.height;
    }

    onItemsResized(entries) {
        if (this.disposed) {
            return;
        }
        for (const entry of entries) {
            const idxAttr = entry.target.getAttribute('data-bv-index');
            if (idxAttr === null) {
                continue;
            }
            const index = parseInt(idxAttr, 10);
            this.pendingMeasure.set(index, this.measureElement(entry.target));
        }
        if (!this.measureScheduled) {
            this.measureScheduled = true;
            requestAnimationFrame(this.flushMeasure);
        }
    }

    flushMeasure() {
        this.measureScheduled = false;
        if (this.disposed || this.pendingMeasure.size === 0) {
            return;
        }
        const indices = [];
        const sizes = [];
        for (const [index, size] of this.pendingMeasure) {
            indices.push(index);
            sizes.push(size);
        }
        this.pendingMeasure.clear();
        this.dotNet.invokeMethodAsync('OnItemsMeasured', indices, sizes);
    }

    // Called by .NET after every render to keep the ResizeObserver subscriptions
    // in sync with the items that are actually in the DOM, and to take an
    // immediate measurement of newly rendered items.
    syncMeasurements() {
        if (this.disposed) {
            return;
        }
        const nodes = this.element.querySelectorAll('[data-bv-index]');
        const present = new Set();
        const indices = [];
        const sizes = [];

        nodes.forEach((node) => {
            const index = parseInt(node.getAttribute('data-bv-index'), 10);
            present.add(index);
            if (this.observed.get(index) !== node) {
                this.observed.set(index, node);
                this.itemObserver.observe(node);
            }
            indices.push(index);
            sizes.push(this.measureElement(node));
        });

        // Stop observing items that have scrolled out of the rendered window.
        for (const [index, node] of this.observed) {
            if (!present.has(index)) {
                this.itemObserver.unobserve(node);
                this.observed.delete(index);
            }
        }

        if (indices.length > 0) {
            this.dotNet.invokeMethodAsync('OnItemsMeasured', indices, sizes);
        }
    }

    scrollToOffset(offset, smooth) {
        if (this.disposed) {
            return;
        }
        const behavior = smooth ? 'smooth' : 'auto';
        if (this.horizontal) {
            this.element.scrollTo({ left: offset, behavior });
        } else {
            this.element.scrollTo({ top: offset, behavior });
        }
    }

    // Adjust the scroll position by delta without emitting a user-scroll event.
    // Used for scroll anchoring after items above the viewport are re-measured.
    adjustScroll(delta) {
        if (this.disposed || delta === 0) {
            return;
        }
        this.suppressScroll = true;
        if (this.horizontal) {
            this.element.scrollLeft += delta;
        } else {
            this.element.scrollTop += delta;
        }
        // Release the suppression after the scroll event has been dispatched.
        requestAnimationFrame(() => { this.suppressScroll = false; });
    }

    dispose() {
        this.disposed = true;
        this.element.removeEventListener('scroll', this.onScroll);
        this.viewportObserver.disconnect();
        this.itemObserver.disconnect();
        this.observed.clear();
        this.pendingMeasure.clear();
    }
}

export function init(element, dotNet, options) {
    const id = ++nextId;
    const instance = new Instance(element, dotNet, options);
    instances.set(id, instance);
    return { id, ...instance.metrics() };
}

export function syncMeasurements(id) {
    instances.get(id)?.syncMeasurements();
}

export function getMetrics(id) {
    const instance = instances.get(id);
    return instance ? instance.metrics() : null;
}

export function scrollToOffset(id, offset, smooth) {
    instances.get(id)?.scrollToOffset(offset, smooth);
}

export function adjustScroll(id, delta) {
    instances.get(id)?.adjustScroll(delta);
}

export function dispose(id) {
    const instance = instances.get(id);
    if (instance) {
        instance.dispose();
        instances.delete(id);
    }
}
