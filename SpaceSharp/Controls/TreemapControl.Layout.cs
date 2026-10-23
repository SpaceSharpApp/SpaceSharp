using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using SpaceSharp.Layout;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Controls;

/// <summary>Layout part of <see cref="TreemapControl"/>.</summary>
public sealed partial class TreemapControl
{
    /// <summary>Each folder's child layout, normalized to its content rectangle (0..1).</summary>
    /// <remarks>Re-running squarify at every zoom would make boxes jump; the cache is only stretched. Cleared on any non-camera change.</remarks>
    private readonly Dictionary<FsNode, CachedChildLayout> _layoutCache = new();

    private sealed record CachedChildLayout(int Cutoff, IReadOnlyList<FsNode> Children, Rect[] Normalized, FsNode? Group, bool GroupMatched);

    // ================================================================ layout

    private void InvalidateLayout()
    {
        _layoutCache.Clear();
        Invalidate();
    }

    private void Invalidate()
    {
        if (_rebuildPending) return;
        _rebuildPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (_rebuildPending) Rebuild();
        }, DispatcherPriority.Render);
    }

    private void EnsureLayout()
    {
        if (_rebuildPending) Rebuild();
    }

    /// <summary>Lays out the visible part of the map and invalidates both layers.</summary>
    private void Rebuild()
    {
        _rebuildPending = false;
        _items.Clear();
        _liveKeys = null;
        _index.Clear();
        _matchedGroups.Clear();
        _subdivided.Clear();
        _siblingSide.Clear();

        double width = Bounds.Width;
        double height = Bounds.Height;
        _viewport = new Rect(0, 0, Math.Max(0, width), Math.Max(0, height));
        _drawClip = _viewport.Inflate(2);

        if (_root is not null && width >= 8 && height >= 8)
        {
            ClampOffset();
            var (w, h) = BaseSize();
            LayoutNode(_root, new Rect(-_offset.X, -_offset.Y, w * _zoom, h * _zoom), 0, 0);
        }

        _mapLayer.InvalidateVisual();

        if (_hoveredNode is not null && !_index.ContainsKey(_hoveredNode))
            _hoveredNode = null;

        DrawOverlay();
        UpdateFocus();
    }

    private void DrawMap(DrawingContext dc)
    {
        dc.DrawRectangle(MapBackground ?? BackgroundBrush, null, _viewport);
        DrawLiveLeaving(dc);
        for (int i = 0; i < _items.Count; i++)
            DrawLiveItem(dc, i);
    }

    private readonly HashSet<FsNode> _subdivided = new();

    private void LayoutNode(FsNode node, Rect bounds, int depth, int branch, bool sole = false, bool flushRight = true, bool flushBottom = true)
    {
        if (!bounds.IntersectsWith(_viewport)) return;

        // collapse single-subfolder chains into one box
        FsNode? chainTop = null;
        var shown = node;
        while (_mergeChains && SingleSubfolder(shown) is { } only)
        {
            chainTop ??= node;
            shown = only;
        }

        bool container = shown.IsDirectory || shown.GroupMembers is not null;
        bool hasHeader = container && !(shown.IsGroup && sole)
            && bounds.Width >= MinHeaderWidth * _labelScale && bounds.Height >= HeaderHeight + MinHeaderBody;
        var item = new TreemapItem(shown, bounds, depth, hasHeader, chainTop, branch, flushRight, flushBottom);
        _items.Add(item);

        for (var n = shown; n is not null; n = n.Parent)
        {
            _index[n] = item;
            if (ReferenceEquals(n, node)) break;
        }

        if (!container || ChildrenOf(shown).Count == 0) return;

        double inset = InsetFor(bounds);
        double top = hasHeader ? HeaderHeight : inset;
        double contentWidth = bounds.Width - 2 * inset;
        double contentHeight = bounds.Height - top - inset;
        if (contentWidth < MinFolderContent || contentHeight < MinFolderContent) return;

        var content = new Rect(bounds.X + inset, bounds.Y + top, contentWidth, contentHeight);
        var layout = ChildLayout(shown, content);
        if (layout.Children.Count == 0) return;
        if (layout.Group is not null && layout.GroupMatched) _matchedGroups.Add(layout.Group);
        _subdivided.Add(shown);

        bool soleChild = layout.Children.Count == 1;
        for (int i = 0; i < layout.Children.Count; i++)
        {
            var n = layout.Normalized[i];
            if (n.IsBlank()) continue;
            var rect = new Rect(
                content.X + n.X * content.Width,
                content.Y + n.Y * content.Height,
                n.Width * content.Width,
                n.Height * content.Height);
            int childBranch = depth == 0 ? i : branch;
            if (rect.Width >= MinChildSize && rect.Height >= MinChildSize)
                LayoutNode(layout.Children[i], rect, depth + 1, childBranch, soleChild, n.X + n.Width >= 0.999, n.Y + n.Height >= 0.999);
        }
    }

    private static IReadOnlyList<FsNode> ChildrenOf(FsNode node) => node.GroupMembers ?? node.Children;

    /// <summary>Returns the container's cached child layout, computing it on first use.</summary>
    /// <remarks>Grouping is decided from each child's share of the whole scan, independent of zoom.</remarks>
    private CachedChildLayout ChildLayout(FsNode folder, Rect content)
    {
        var all = ChildrenOf(folder);
        int cutoff;
        if (!GroupSmall) cutoff = all.Count;
        else
        {
            cutoff = folder.IsGroup ? 0 : GroupCutoff(all);
            if (cutoff == 0)
                cutoff = Grouping.CutoffOnScreen(all, _measure, folder.SizeFor(_measure), content.Width, content.Height, VisibleArea);
            if (cutoff == 0 && folder.IsGroup) cutoff = -1;
        }
        if (_layoutCache.TryGetValue(folder, out var cached))
        {
            if (cached.Cutoff == cutoff) return cached;
            var stale = cached.Group;
            while (stale is not null && _layoutCache.Remove(stale, out var inner)) stale = inner.Group;
        }

        if (cutoff < 0)
        {
            cached = new CachedChildLayout(cutoff, Array.Empty<FsNode>(), Array.Empty<Rect>(), null, false);
            _layoutCache[folder] = cached;
            return cached;
        }

        IReadOnlyList<FsNode> children = all;
        FsNode? group = null;
        bool groupMatched = false;
        if (cutoff < all.Count)
        {
            var grouped = GroupSmallChildren(folder, all, cutoff, out group, out groupMatched);
            if (grouped is not null) children = grouped;
        }

        var normalized = new Rect[children.Count];
        int index = 0;
        Squarify.Layout(children, new LayoutRect(0, 0, content.Width, content.Height), _measure, (_, rect) =>
        {
            normalized[index++] = new Rect(rect.X / content.Width, rect.Y / content.Height,
                rect.Width / content.Width, rect.Height / content.Height);
        }, OrientationBias);
        for (; index < normalized.Length; index++) normalized[index] = Rects.Empty;

        cached = new CachedChildLayout(cutoff, children, normalized, group, groupMatched);
        _layoutCache[folder] = cached;
        return cached;
    }

    /// <summary>Reference map area that grouping is judged against.</summary>
    private const double ReferenceArea = 1200.0 * 760.0;

    private int GroupCutoff(IReadOnlyList<FsNode> children)
    {
        double whole = _root?.SizeFor(_measure) ?? 0;
        if (whole <= 0) return children.Count;
        return Grouping.Cutoff(children, _measure, ReferenceArea / whole, GroupBelowArea);
    }

    /// <summary>Replaces children from <paramref name="cutoff"/> on with one group node.</summary>
    private IReadOnlyList<FsNode>? GroupSmallChildren(FsNode folder, IReadOnlyList<FsNode> children, int cutoff, out FsNode? group, out bool groupMatched)
    {
        group = null;
        groupMatched = false;

        long size = 0, allocated = 0;
        int files = 0, count = 0, folders = 0;
        for (int i = cutoff; i < children.Count; i++)
        {
            var c = children[i];
            if (c.SizeFor(_measure) <= 0) continue;
            size += c.Size;
            allocated += c.Allocated;
            files += c.FileCount;
            count++;
            if (c.IsDirectory) folders++;
        }
        if (count < 2) return null;

        string kind = folders == 0 ? Strings.Get("Group_Files") : folders == count ? Strings.Get("Group_Folders") : Strings.Get("Group_Items");
        var members = new List<FsNode>(children.Count - cutoff);
        for (int i = cutoff; i < children.Count; i++) if (children[i].SizeFor(_measure) > 0) members.Add(children[i]);
        group = new FsNode($"{count:N0} {kind}", NodeKind.Group, folder.IsGroup ? folder.Parent : folder)
        {
            Size = size,
            Allocated = allocated,
            FileCount = files,
            GroupMembers = members
        };
        if (_filterMatches is not null)
        {
            for (int i = cutoff; i < children.Count; i++)
            {
                if (_filterMatches.Contains(children[i]))
                {
                    groupMatched = true;
                    break;
                }
            }
        }

        var list = new List<FsNode>(cutoff + 1);
        for (int i = 0; i < cutoff; i++) list.Add(children[i]);
        list.Add(group);
        list.Sort((a, b) => b.SizeFor(_measure).CompareTo(a.SizeFor(_measure)));
        return list;
    }

    /// <summary>The folder's dominant subfolder, if it holds at least 97% of the size.</summary>
    private FsNode? SingleSubfolder(FsNode node)
    {
        if (!node.IsDirectory || node.Children.Count == 0) return null;
        var first = node.Children[0]; // largest first
        long total = node.SizeFor(_measure);
        if (!first.IsDirectory || total <= 0) return null;
        return first.SizeFor(_measure) * 100 >= total * 97 ? first : null;
    }

    private void UpdateFocus()
    {
        FsNode? focus = null;

        if (_pinnedFocus is not null && _index.ContainsKey(_pinnedFocus))
        {
            focus = _pinnedFocus;
        }
        else if (_root is not null)
        {
            // deepest folder under the center covering at least half the window
            var center = new Point(_viewport.Width / 2, _viewport.Height / 2);
            double viewArea = _viewport.Width * _viewport.Height;
            foreach (var item in _items)
            {
                if (!item.Node.IsDirectory || !item.Bounds.Contains(center)) continue;
                var visible = Rects.Intersect(item.Bounds, _viewport);
                if (!visible.IsBlank() && visible.Width * visible.Height >= 0.5 * viewArea)
                    focus = item.Node;
            }
            focus ??= _root;
        }

        if (ReferenceEquals(focus, _focus)) return;
        _focus = focus;
        FocusChanged?.Invoke(this, EventArgs.Empty);
    }
}
