using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SpaceSharp.Layout;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Controls;

/// <summary>Part of <see cref="TreemapControl"/>. TreemapControl.cs has the overview.</summary>
public sealed partial class TreemapControl
{
    /// <summary>
    /// Each folder's child layout, in coordinates relative to its content rectangle (0..1). Title bars and
    /// insets are a fixed number of pixels, so a folder's content area changes shape slightly as you zoom;
    /// re-running squarify on the new shape can flip a row from horizontal to vertical and make boxes
    /// jump around. Keeping the first layout and only stretching it makes zoom continuous. The cache is
    /// dropped whenever something other than the camera changes (tree, measure, style, window size).
    /// </summary>
    private readonly Dictionary<FsNode, CachedChildLayout> _layoutCache = new();

    private sealed record CachedChildLayout(int Cutoff, IReadOnlyList<FsNode> Children, Rect[] Normalized, FsNode? Group, bool GroupMatched);

    // ================================================================ layout

    /// <summary>Invalidate for changes that alter the layout itself, not only the camera.</summary>
    private void InvalidateLayout()
    {
        _layoutCache.Clear();
        Invalidate();
    }

    private void Invalidate()
    {
        if (_rebuildPending) return;
        _rebuildPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            if (_rebuildPending) Rebuild();
        }));
    }

    private void EnsureLayout()
    {
        if (_rebuildPending) Rebuild();
    }

    private void Rebuild()
    {
        _rebuildPending = false;
        _items.Clear();
        _index.Clear();
        _matchedGroups.Clear();

        double width = ActualWidth;
        double height = ActualHeight;
        _viewport = new Rect(0, 0, Math.Max(0, width), Math.Max(0, height));
        _drawClip = Rect.Inflate(_viewport, 2, 2);

        if (_root is not null && width >= 8 && height >= 8)
        {
            ClampOffset();
            var (w, h) = BaseSize();
            LayoutNode(_root, new Rect(-_offset.X, -_offset.Y, w * _zoom, h * _zoom), 0, 0);
        }

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        using (var dc = _mapVisual.RenderOpen())
        {
            dc.DrawRectangle(MapBackground ?? BackgroundBrush, null, _viewport);
            foreach (var item in _items)
                DrawItem(dc, item, pixelsPerDip);
        }

        if (_hoveredNode is not null && !_index.ContainsKey(_hoveredNode))
            _hoveredNode = null;

        DrawOverlay();
        UpdateFocus();
    }

    private void LayoutNode(FsNode node, Rect bounds, int depth, int branch)
    {
        if (!bounds.IntersectsWith(_viewport)) return;

        // A folder whose only content is one folder (Users > Alex > AppData > ...) is drawn as ONE
        // box with a combined title, instead of a stack of nested frames and title bars.
        FsNode? chainTop = null;
        var shown = node;
        while (_mergeChains && SingleSubfolder(shown) is { } only)
        {
            chainTop ??= node;
            shown = only;
        }

        bool container = shown.IsDirectory || shown.GroupMembers is not null;
        bool hasHeader = container && bounds.Width >= MinHeaderWidth * _labelScale && bounds.Height >= MinHeaderHeight * _labelScale;
        var item = new TreemapItem(shown, bounds, depth, hasHeader, chainTop, branch);
        _items.Add(item);

        // Every folder of the chain maps to the same box, so focus/selection/zoom still work for each.
        for (var n = shown; n is not null; n = n.Parent)
        {
            _index[n] = item;
            if (ReferenceEquals(n, node)) break;
        }

        if (!container || ChildrenOf(shown).Count == 0) return;

        // Small boxes get thinner frames; tiny ones aren't subdivided at all, so narrow folders
        // don't turn into a pile of nested outlines.
        double inset = InsetFor(bounds);
        double top = hasHeader ? HeaderHeight : inset;
        double contentWidth = bounds.Width - 2 * inset;
        double contentHeight = bounds.Height - top - inset;
        if (contentWidth < MinFolderContent || contentHeight < MinFolderContent) return;

        var content = new Rect(bounds.X + inset, bounds.Y + top, contentWidth, contentHeight);
        var layout = ChildLayout(shown, content);
        if (layout.Group is not null && layout.GroupMatched) _matchedGroups.Add(layout.Group);

        for (int i = 0; i < layout.Children.Count; i++)
        {
            var n = layout.Normalized[i];
            if (n.IsEmpty) continue;
            var rect = new Rect(
                content.X + n.X * content.Width,
                content.Y + n.Y * content.Height,
                n.Width * content.Width,
                n.Height * content.Height);
            // Children of the root define the branches; everything below inherits its branch.
            int childBranch = depth == 0 ? i : branch;
            if (rect.Width >= MinChildSize && rect.Height >= MinChildSize)
                LayoutNode(layout.Children[i], rect, depth + 1, childBranch);
        }
    }

    /// <summary>A folder's children, or the members a group pseudo node stands for.</summary>
    private static IReadOnlyList<FsNode> ChildrenOf(FsNode node) => node.GroupMembers ?? node.Children;

    /// <summary>
    /// The container's child layout, from the cache after the first time. Which children are grouped is
    /// decided from the data alone (their share of the whole scan), never from the current zoom, so a box
    /// keeps its place however far you zoom: a "312 files" group that gets room lays its members out inside
    /// its own rectangle instead of the parent being laid out again.
    /// </summary>
    private CachedChildLayout ChildLayout(FsNode folder, Rect content)
    {
        var all = ChildrenOf(folder);
        // Groups are laid out flat: their members are the small items by definition, and grouping them again
        // would only wrap the same box in another header. A cutoff of 0 would group every child, which is the
        // folder itself; leave those flat too.
        int cutoff = _groupSmall && !folder.IsGroup ? GroupCutoff(all) : all.Count;
        if (cutoff == 0) cutoff = all.Count;
        if (_layoutCache.TryGetValue(folder, out var cached) && cached.Cutoff == cutoff)
            return cached;

        IReadOnlyList<FsNode> children = all;
        FsNode? group = null;
        bool groupMatched = false;
        if (cutoff < all.Count)
        {
            var grouped = GroupSmallChildren(folder, all, cutoff, out group, out groupMatched);
            if (grouped is not null) children = grouped;
            // Fewer than two small children: nothing to group; the requested cutoff stays the cache key.
        }

        // Lay out in the content's shape, then normalize so the same layout can be stretched to any zoom.
        var normalized = new Rect[children.Count];
        int index = 0;
        Squarify.Layout(children, new Rect(0, 0, content.Width, content.Height), _measure, (_, rect) =>
        {
            normalized[index++] = new Rect(rect.X / content.Width, rect.Y / content.Height,
                rect.Width / content.Width, rect.Height / content.Height);
        }, OrientationBias);
        // Zero-sized children get no rectangle from squarify.
        for (; index < normalized.Length; index++) normalized[index] = Rect.Empty;

        cached = new CachedChildLayout(cutoff, children, normalized, group, groupMatched);
        _layoutCache[folder] = cached;
        return cached;
    }

    /// <summary>The whole map's area at a reference size; grouping is judged against it, not the live window or zoom.</summary>
    private const double ReferenceArea = 1200.0 * 760.0;

    /// <summary>
    /// Index of the first child that would get fewer than <see cref="GroupBelowArea"/> pixels if the whole
    /// scan filled the reference area at zoom 1. A pure function of the data (and density), so stable.
    /// </summary>
    private int GroupCutoff(IReadOnlyList<FsNode> children)
    {
        double whole = _root?.SizeFor(_measure) ?? 0;
        if (whole <= 0) return children.Count;
        double pixelsPerByte = ReferenceArea / whole;
        for (int i = 0; i < children.Count; i++)
            if (children[i].SizeFor(_measure) * pixelsPerByte < GroupBelowArea)
                return i;
        return children.Count;
    }

    /// <summary>
    /// A folder with hundreds of similar files (a photo shoot, a cache) would become a grid of tiny
    /// boxes. Children that would get less than <see cref="GroupBelowArea"/> pixels are replaced by one
    /// "312 files" box. Zooming in gives them more pixels, so they appear individually again.
    /// </summary>
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
        group = new FsNode($"{count:N0} {kind}", folder.FullPath, NodeKind.Group, folder.IsGroup ? folder.Parent : folder)
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

    /// <summary>
    /// The folder's dominant child, if it is a folder holding at least 97% of the size. Steam › steamapps › common
    /// still merges when Steam has a few small files next to the big folder.
    /// </summary>
    private FsNode? SingleSubfolder(FsNode node)
    {
        if (!node.IsDirectory || node.Children.Count == 0) return null;
        var first = node.Children[0]; // sorted by size, largest first
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
            // Deepest folder under the window center that covers at least half the window.
            var center = new Point(_viewport.Width / 2, _viewport.Height / 2);
            double viewArea = _viewport.Width * _viewport.Height;
            foreach (var item in _items)
            {
                if (!item.Node.IsDirectory || !item.Bounds.Contains(center)) continue;
                var visible = Rect.Intersect(item.Bounds, _viewport);
                if (!visible.IsEmpty && visible.Width * visible.Height >= 0.5 * viewArea)
                    focus = item.Node;
            }
            focus ??= _root;
        }

        if (ReferenceEquals(focus, _focus)) return;
        _focus = focus;
        FocusChanged?.Invoke(this, EventArgs.Empty);
    }

}
