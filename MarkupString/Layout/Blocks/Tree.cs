using System.Collections.Immutable;

namespace MarkupString.Layout;

/// <summary>
/// Items and the items under them, each level drawn under its parent with guide lines —
/// <c>├─ </c>, <c>└─ </c> — joining them. The top level sits at the left edge with no guide.
/// </summary>
/// <param name="Items">The top-level items.</param>
public sealed record Tree(ImmutableArray<TreeItem> Items) : Block
{
	/// <inheritdoc/>
	public override Block MapText(Func<MarkupText, MarkupText> map) =>
		this with { Items = BlockText.Map(Items, map) };

	/// <summary>The characters the guide lines are drawn with; unset, the theme's.</summary>
	public TreeGuide? Guide { get; init; }

	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines)
	{
		if (Items.IsDefaultOrEmpty) return;
		var guide = context.Paint(context.Guide(Guide));
		foreach (var item in Items) DrawItem(item, MarkupText.Empty, null, guide, context, width, lines);
	}

	/// <inheritdoc/>
	public override void DrawLinear(LayoutContext context, int width, IList<MarkupText> lines)
	{
		if (Items.IsDefaultOrEmpty) return;
		foreach (var item in Items) Speak(item, 0, context, width, lines);
	}

	/// <summary>
	/// One item at <paramref name="prefix"/>: its first line after the branch or last guide (none at the
	/// top level), the rest after the guide that carries its level on, then its children one level in.
	/// </summary>
	private static void DrawItem(TreeItem item, MarkupText prefix, bool? last, TreeGuide guide, LayoutContext context, int width, IList<MarkupText> lines)
	{
		var head = last switch { null => MarkupText.Empty, true => guide.Last, false => guide.Branch };
		var carry = last switch { null => MarkupText.Empty, true => guide.Blank, false => guide.Pipe };
		var lead = MarkupText.Concat([prefix, head]);
		var content = context.Lines(item.Content, width - lead.DisplayWidth);
		for (var i = 0; i < content.Count; i++)
			lines.Add(BlockText.Fit(MarkupText.Concat([i == 0 ? lead : MarkupText.Concat([prefix, carry]), content[i]]), width));

		if (item.Children.IsDefaultOrEmpty) return;
		var inner = MarkupText.Concat([prefix, carry]);
		for (var i = 0; i < item.Children.Length; i++)
			DrawItem(item.Children[i], inner, i == item.Children.Length - 1, guide, context, width, lines);
	}

	/// <summary>A reader hears the levels as indentation, not as line drawing: two spaces a level.</summary>
	private static void Speak(TreeItem item, int depth, LayoutContext context, int width, IList<MarkupText> lines)
	{
		var indent = BlockText.Blank(depth);
		foreach (var line in context.Lines(item.Content, width - depth)) lines.Add(MarkupText.Concat([indent, line]));
		if (item.Children.IsDefaultOrEmpty) return;
		foreach (var child in item.Children) Speak(child, depth + 2, context, width, lines);
	}
}

/// <summary>One item of a <see cref="Tree"/> and the items under it.</summary>
/// <param name="Content">What the item shows.</param>
/// <param name="Children">The items under it, or none.</param>
public sealed record TreeItem(Block Content, ImmutableArray<TreeItem> Children = default);
