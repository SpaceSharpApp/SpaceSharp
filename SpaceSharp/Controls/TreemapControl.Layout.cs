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

    /// <summary>A folder's squarified children, stored once and stretched to the folder's box at any zoom.</summary>
    /// <param name="Cutoff">How many children were laid out individually before grouping; -1 marks a group whose members are all too small
    /// to show, so nothing is laid out.</param>
    /// <param name="Children">The nodes laid out, largest first, with the group node (if any) in its place by size; may differ from the
    /// folder's own children.</param>
    /// <param name="Normalized">One rectangle per entry of <paramref name="Children"/>, in 0..1 coordinates of the folder's content box;
    /// <see cref="Rects.Empty"/> for a child squarify did not place.</param>
    /// <param name="Group">The group node standing in for the small children, or null when none were grouped.</param>
    /// <param name="GroupMatched">True when at least one grouped child passes the current filter, so the group box is drawn undimmed.</param>
    private sealed record CachedChildLayout(int Cutoff, IReadOnlyList<FsNode> Children, Rect[] Normalized, FsNode? Group, bool GroupMatched);

    // ================================================================ layout

    /// <summary>Throws away every cached child layout and schedules a rebuild; for anything but a camera move.</summary>
    private void InvalidateLayout()
    {
        _layoutCache.Clear();
        Invalidate();
    }

    /// <summary>Schedules one rebuild on the UI thread at render priority; repeated calls before it runs are folded into it.</summary>
    private void Invalidate()
    {
        if (_rebuildPending)
        {
            return;
        }
        _rebuildPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (_rebuildPending)
            {
                Rebuild();
            }
        }, DispatcherPriority.Render);
    }

    /// <summary>Runs a scheduled rebuild now, so callers that read item bounds see the current layout.</summary>
    private void EnsureLayout()
    {
        if (_rebuildPending)
        {
            Rebuild();
        }
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
            (double baseWidth, double baseHeight) = BaseSize();
            var rootBounds = new Rect(-_offset.X, -_offset.Y, baseWidth * _zoom, baseHeight * _zoom);
            LayoutNode(_root, rootBounds, 0, 0);
        }

        _mapLayer.InvalidateVisual();

        if (_hoveredNode is not null && !_index.ContainsKey(_hoveredNode))
        {
            _hoveredNode = null;
        }

        DrawOverlay();
        UpdateFocus();
    }

    /// <summary>Paints the map layer: the background, then any boxes fading out, then every laid-out box in layout order (parents before
    /// children).</summary>
    /// <param name="dc">The map layer's drawing context for this render pass.</param>
    private void DrawMap(DrawingContext dc)
    {
        dc.DrawRectangle(MapBackground ?? BackgroundBrush, null, _viewport);
        DrawLiveLeaving(dc);
        for (int i = 0; i < _items.Count; i++)
        {
            DrawLiveItem(dc, i);
        }
    }

    /// <summary>Containers whose children were laid out this pass; a group in this set draws no label, its members do.</summary>
    private readonly HashSet<FsNode> _subdivided = new();

    /// <summary>Adds the node's box to the layout and recurses into its children, skipping anything outside the viewport or below the
    /// minimum size.</summary>
    /// <param name="node">The folder, file, group or free-space node to place; a folder that is a single-subfolder chain is collapsed and
    /// the deepest folder is shown.</param>
    /// <param name="bounds">The node's box in control pixels at the current zoom; may lie partly outside the window.</param>
    /// <param name="depth">Nesting level, 0 for the root.</param>
    /// <param name="branch">Index of the top-level folder this box descends from, for coloring by branch; the root passes 0 and its
    /// children start their own.</param>
    /// <param name="sole">True when the node is its parent's only laid-out child; a group that fills its parent then gets no title bar of
    /// its own.</param>
    /// <param name="flushRight">True when the box reaches its parent's right content edge, so no grid strip is drawn there.</param>
    /// <param name="flushBottom">Same for the bottom edge.</param>
    private void LayoutNode(
        FsNode node,
        Rect bounds,
        int depth,
        int branch,
        bool sole = false,
        bool flushRight = true,
        bool flushBottom = true)
    {
        if (!bounds.IntersectsWith(_viewport))
        {
            return;
        }

        // collapse single-subfolder chains into one box
        FsNode? chainTop = null;
        FsNode shown = node;
        while (_mergeChains && SingleSubfolder(shown) is { } onlySubfolder)
        {
            chainTop ??= node;
            shown = onlySubfolder;
        }

        bool container = shown.IsDirectory || shown.GroupMembers is not null;
        bool hasHeader = container && !(shown.IsGroup && sole)
            && bounds.Width >= MinHeaderWidth * _labelScale && bounds.Height >= HeaderHeight + MinHeaderBody;
        var item = new TreemapItem(shown, bounds, depth, hasHeader, chainTop, branch, flushRight, flushBottom);
        _items.Add(item);

        // Every folder merged into the chain maps to the one box that shows it.
        for (FsNode? chainNode = shown; chainNode is not null; chainNode = chainNode.Parent)
        {
            _index[chainNode] = item;
            if (ReferenceEquals(chainNode, node))
            {
                break;
            }
        }

        if (!container || ChildrenOf(shown).Count == 0)
        {
            return;
        }

        double inset = InsetFor(bounds);
        double top = hasHeader ? HeaderHeight : inset;
        double contentWidth = bounds.Width - 2 * inset;
        double contentHeight = bounds.Height - top - inset;
        if (contentWidth < MinFolderContent || contentHeight < MinFolderContent)
        {
            return;
        }

        var content = new Rect(bounds.X + inset, bounds.Y + top, contentWidth, contentHeight);
        CachedChildLayout layout = ChildLayout(shown, content);
        if (layout.Children.Count == 0)
        {
            return;
        }

        if (layout.Group is not null && layout.GroupMatched)
        {
            _matchedGroups.Add(layout.Group);
        }
        _subdivided.Add(shown);

        bool soleChild = layout.Children.Count == 1;
        for (int i = 0; i < layout.Children.Count; i++)
        {
            Rect normalized = layout.Normalized[i];
            if (normalized.IsBlank())
            {
                continue;
            }

            var childBounds = new Rect(
                content.X + normalized.X * content.Width,
                content.Y + normalized.Y * content.Height,
                normalized.Width * content.Width,
                normalized.Height * content.Height);
            if (childBounds.Width < MinChildSize || childBounds.Height < MinChildSize)
            {
                continue;
            }

            // Top-level children start the color branches; deeper boxes inherit theirs.
            int childBranch = depth == 0 ? i : branch;
            bool childFlushRight = normalized.X + normalized.Width >= 0.999;
            bool childFlushBottom = normalized.Y + normalized.Height >= 0.999;
            LayoutNode(
                layout.Children[i], childBounds, depth + 1, childBranch, soleChild, childFlushRight, childFlushBottom);
        }
    }

    /// <summary>The nodes a container lays out: a group's members, otherwise the folder's children.</summary>
    /// <param name="node">A folder or group node; for a file this is its (empty) child list.</param>
    /// <returns>The list to lay out, largest first as the tree is sorted; never null.</returns>
    private static IReadOnlyList<FsNode> ChildrenOf(FsNode node) => node.GroupMembers ?? node.Children;

    /// <summary>Returns the container's cached child layout, computing it on first use.</summary>
    /// <remarks>Grouping is decided from each child's share of the whole scan, independent of zoom.</remarks>
    /// <param name="folder">The folder or group whose children are laid out.</param>
    /// <param name="content">The area inside the folder's frame and title bar, in control pixels at the current zoom; only its aspect ratio
    /// and on-screen size matter, the result is normalized.</param>
    /// <returns>The cached layout when its grouping cutoff still holds, else a fresh one that replaces it; never null, possibly with no
    /// children.</returns>
    private CachedChildLayout ChildLayout(FsNode folder, Rect content)
    {
        IReadOnlyList<FsNode> allChildren = ChildrenOf(folder);
        int cutoff;
        if (!GroupSmall)
        {
            cutoff = allChildren.Count;
        }
        else
        {
            cutoff = folder.IsGroup ? 0 : GroupCutoff(allChildren);
            if (cutoff == 0)
            {
                cutoff = Grouping.CutoffOnScreen(
                    allChildren, _measure, folder.SizeFor(_measure), content.Width, content.Height, VisibleArea);
            }

            // A group whose members are all too small to show lays out nothing; -1 keeps that cached.
            if (cutoff == 0 && folder.IsGroup)
            {
                cutoff = -1;
            }
        }

        if (_layoutCache.TryGetValue(folder, out CachedChildLayout? cached))
        {
            if (cached.Cutoff == cutoff)
            {
                return cached;
            }

            // A new cutoff makes a new group node, so the old group's own cached layouts go with it.
            FsNode? staleGroup = cached.Group;
            while (staleGroup is not null && _layoutCache.Remove(staleGroup, out CachedChildLayout? staleLayout))
            {
                staleGroup = staleLayout.Group;
            }
        }

        if (cutoff < 0)
        {
            cached = new CachedChildLayout(cutoff, Array.Empty<FsNode>(), Array.Empty<Rect>(), null, false);
            _layoutCache[folder] = cached;
            return cached;
        }

        IReadOnlyList<FsNode> children = allChildren;
        FsNode? group = null;
        bool groupMatched = false;
        if (cutoff < allChildren.Count)
        {
            IReadOnlyList<FsNode>? grouped =
                GroupSmallChildren(folder, allChildren, cutoff, out group, out groupMatched);
            if (grouped is not null)
            {
                children = grouped;
            }
        }

        // Squarify reports the rectangles in child order; store them relative to the content box.
        var normalized = new Rect[children.Count];
        int index = 0;
        var contentBox = new LayoutRect(0, 0, content.Width, content.Height);
        Squarify.Layout(children, contentBox, _measure, (_, rect) =>
        {
            normalized[index++] = new Rect(rect.X / content.Width, rect.Y / content.Height,
                rect.Width / content.Width, rect.Height / content.Height);
        }, OrientationBias);

        // Children squarify did not place (zero size, or no room left) get an empty rectangle.
        for (; index < normalized.Length; index++)
        {
            normalized[index] = Rects.Empty;
        }

        cached = new CachedChildLayout(cutoff, children, normalized, group, groupMatched);
        _layoutCache[folder] = cached;
        return cached;
    }

    /// <summary>Reference map area that grouping is judged against.</summary>
    private const double ReferenceArea = 1200.0 * 760.0;

    /// <summary>How many of a folder's children stay individual boxes on the reference map, judged by their share of the whole scan.</summary>
    /// <param name="children">The folder's children, largest first.</param>
    /// <returns>The index from which children are grouped; the full count (nothing grouped) when the scan has no size, 0 when all of them
    /// are below the group area.</returns>
    private int GroupCutoff(IReadOnlyList<FsNode> children)
    {
        double whole = _root?.SizeFor(_measure) ?? 0;
        if (whole <= 0)
        {
            return children.Count;
        }
        return Grouping.Cutoff(children, _measure, ReferenceArea / whole, GroupBelowArea);
    }

    /// <summary>Replaces children from <paramref name="cutoff"/> on with one group node.</summary>
    /// <param name="folder">The container being laid out; a group's members hang off the real folder above it so their paths stay right.</param>
    /// <param name="children">All children, largest first; the ones before <paramref name="cutoff"/> are kept as they are.</param>
    /// <param name="cutoff">Index of the first child to group, 0 to Count - 1.</param>
    /// <param name="group">The new group node, named "n files", "n folders" or "n items", sized as the sum of its members; null when no
    /// group was made.</param>
    /// <param name="groupMatched">True when a grouped child is in the current filter's matches, so the group box is not dimmed.</param>
    /// <returns>The kept children plus the group, re-sorted largest first; null when fewer than two children have a size, since one small
    /// box is cheaper than a group standing in for it.</returns>
    private IReadOnlyList<FsNode>? GroupSmallChildren(
        FsNode folder,
        IReadOnlyList<FsNode> children,
        int cutoff,
        out FsNode? group,
        out bool groupMatched)
    {
        group = null;
        groupMatched = false;

        long groupSize = 0;
        long groupAllocated = 0;
        int groupFileCount = 0;
        int memberCount = 0;
        int folderCount = 0;
        for (int i = cutoff; i < children.Count; i++)
        {
            FsNode child = children[i];
            if (child.SizeFor(_measure) <= 0)
            {
                continue;
            }

            groupSize += child.Size;
            groupAllocated += child.Allocated;
            groupFileCount += child.FileCount;
            memberCount++;
            if (child.IsDirectory)
            {
                folderCount++;
            }
        }

        // One small child is cheaper to draw than a group box standing in for it.
        if (memberCount < 2)
        {
            return null;
        }

        string kind = folderCount == 0
            ? Strings.Get("Group_Files")
            : folderCount == memberCount ? Strings.Get("Group_Folders") : Strings.Get("Group_Items");
        var members = new List<FsNode>(children.Count - cutoff);
        for (int i = cutoff; i < children.Count; i++)
        {
            if (children[i].SizeFor(_measure) > 0)
            {
                members.Add(children[i]);
            }
        }

        // A group inside a group hangs off the real folder, so its path stays right.
        FsNode? groupParent = folder.IsGroup ? folder.Parent : folder;
        group = new FsNode($"{memberCount:N0} {kind}", NodeKind.Group, groupParent)
        {
            Size = groupSize,
            Allocated = groupAllocated,
            FileCount = groupFileCount,
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

        // The group takes its place in size order among the children it replaces.
        var withGroup = new List<FsNode>(cutoff + 1);
        for (int i = 0; i < cutoff; i++)
        {
            withGroup.Add(children[i]);
        }
        withGroup.Add(group);
        withGroup.Sort((left, right) => right.SizeFor(_measure).CompareTo(left.SizeFor(_measure)));
        return withGroup;
    }

    /// <summary>The folder's dominant subfolder, if it holds at least 97% of the size.</summary>
    /// <param name="node">Any node; files, empty folders and nodes without a size give null.</param>
    /// <returns>The largest child when it is a folder holding at least 97% of <paramref name="node"/> by the current size measure, else
    /// null.</returns>
    private FsNode? SingleSubfolder(FsNode node)
    {
        if (!node.IsDirectory || node.Children.Count == 0)
        {
            return null;
        }
        FsNode first = node.Children[0]; // largest first
        long total = node.SizeFor(_measure);
        if (!first.IsDirectory || total <= 0)
        {
            return null;
        }
        return first.SizeFor(_measure) * 100 >= total * 97 ? first : null;
    }

    /// <summary>Recomputes <see cref="FocusedFolder"/> after a layout: the pinned folder while it is on screen, else the deepest folder
    /// under the window center that covers at least half the window, else the root. Raises <see cref="FocusChanged"/> only on a
    /// change.</summary>
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
            foreach (TreemapItem item in _items)
            {
                if (!item.Node.IsDirectory || !item.Bounds.Contains(center))
                {
                    continue;
                }
                Rect visible = Rects.Intersect(item.Bounds, _viewport);
                if (!visible.IsBlank() && visible.Width * visible.Height >= 0.5 * viewArea)
                {
                    focus = item.Node;
                }
            }
            focus ??= _root;
        }

        if (ReferenceEquals(focus, _focus))
        {
            return;
        }
        _focus = focus;
        FocusChanged?.Invoke(this, EventArgs.Empty);
    }
}
