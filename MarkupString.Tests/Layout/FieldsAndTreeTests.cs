using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

public class FieldsAndTreeTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml();

	private static MarkupText P(string text) => MarkupText.Plain(text);

	private static string[] Lines(LayoutNode node, int width, BlockRenderOptions? options = null) =>
		[.. BlockLayout.Lines(node, width, options).Select(line => line.ToPlainText().TrimEnd())];

	private static FieldsNode Fields(FieldsOptions? options, params (string Label, string Value)[] pairs) =>
		new([.. pairs.Select(pair => new Field(P(pair.Label), new TextNode(P(pair.Value))))], options ?? FieldsOptions.Default);

	private static readonly (string, string)[] Sheet =
		[("Sex", "Male"), ("Species", "Human"), ("Job", "Dark Warrior"), ("Origin", "Super Robot Wars AG")];

	[Test]
	public async Task Fields_LineTheValuesUp()
		=> await Assert.That(Lines(Fields(null, Sheet), 40)).IsEquivalentTo(new[]
		{
			"Sex:     Male",
			"Species: Human",
			"Job:     Dark Warrior",
			"Origin:  Super Robot Wars AG",
		});

	[Test]
	public async Task Fields_RightAlignedLabels()
		=> await Assert.That(Lines(Fields(FieldsOptions.Default with { LabelAlignment = Alignment.Right }, Sheet[..2]), 40)).IsEquivalentTo(new[]
		{
			"    Sex: Male",
			"Species: Human",
		});

	[Test]
	public async Task Fields_LeaderFillsToTheSeparator()
		=> await Assert.That(Lines(Fields(FieldsOptions.Default with { Leader = P(".") }, Sheet[..2]), 40)).IsEquivalentTo(new[]
		{
			"Sex....: Male",
			"Species: Human",
		});

	/// <summary>A value too long for its column wraps under itself, not under the label.</summary>
	[Test]
	public async Task Fields_ValueWrapsWithAHangingIndent()
		=> await Assert.That(Lines(Fields(null, ("Quote", "Hooooo? said the machine child, laughing")), 24)).IsEquivalentTo(new[]
		{
			"Quote: Hooooo? said the",
			"       machine child,",
			"       laughing",
		});

	[Test]
	public async Task Fields_TooNarrow_PutEachLabelOverItsValue()
		=> await Assert.That(Lines(Fields(null, ("Species", "Human")), 14)).IsEquivalentTo(new[]
		{
			"Species:",
			"  Human",
		});

	[Test]
	public async Task Fields_DealtIntoColumns_DownFirst()
		=> await Assert.That(Lines(Fields(FieldsOptions.Default with { Columns = 2 }, Sheet), 60)).IsEquivalentTo(new[]
		{
			"Sex:     Male" + new string(' ', 19) + "Job:    Dark Warrior",
			"Species: Human" + new string(' ', 18) + "Origin: Super Robot Wars AG",
		});

	[Test]
	public async Task Fields_ColumnsStackWhenNarrow()
		=> await Assert.That(Lines(Fields(FieldsOptions.Default with { Columns = 2 }, Sheet), 30)).IsEquivalentTo(new[]
		{
			"Sex:     Male",
			"Species: Human",
			"Job:    Dark Warrior",
			"Origin: Super Robot Wars AG",
		});

	[Test]
	public async Task Fields_InHtml_AreADefinitionList()
	{
		var html = BlockLayout.Build(Fields(null, Sheet[..1]), 30).Render(MarkupFormat.Html, Registry);

		await Assert.That(html).Contains("<dl class=\"ms-fields\"><div class=\"ms-field\"><dt>Sex:</dt><dd><div class=\"ms-text\">Male</div></dd></div></dl>");
	}

	[Test]
	public async Task Fields_Linear_ReadLabelThenValue()
		=> await Assert.That(Lines(Fields(FieldsOptions.Default with { Columns = 2 }, Sheet[..2]), 40, new BlockRenderOptions { Linear = true }))
			.IsEquivalentTo(new[] { "Sex: Male", "Species: Human" });

	private static TreeNode Channels(TreeGuide guide) => new(
	[
		new TreeItem(new TextNode(P("Channels")),
		[
			new TreeItem(new TextNode(P("Public")),
			[
				new TreeItem(new TextNode(P("+chat"))),
				new TreeItem(new TextNode(P("+ooc"))),
			]),
			new TreeItem(new TextNode(P("Staff")),
			[
				new TreeItem(new TextNode(P("+admin"))),
			]),
		]),
	], guide);

	[Test]
	public async Task Tree_DrawsGuides()
		=> await Assert.That(Lines(Channels(TreeGuide.Line), 30)).IsEquivalentTo(new[]
		{
			"Channels",
			"├─ Public",
			"│  ├─ +chat",
			"│  └─ +ooc",
			"└─ Staff",
			"   └─ +admin",
		});

	[Test]
	public async Task Tree_AsciiOnly_KeepsTheLastBranchDistinct()
		=> await Assert.That(Lines(Channels(TreeGuide.Rounded), 30, new BlockRenderOptions { AsciiOnly = true })).IsEquivalentTo(new[]
		{
			"Channels",
			"|- Public",
			"|  |- +chat",
			"|  `- +ooc",
			"`- Staff",
			"   `- +admin",
		});

	[Test]
	public async Task Tree_LongItemWrapsUnderItself()
	{
		var tree = new TreeNode([new TreeItem(new TextNode(P("Root")), [new TreeItem(new TextNode(P("a long item that wraps"))), new TreeItem(new TextNode(P("b")))])], TreeGuide.Line);

		await Assert.That(Lines(tree, 16)).IsEquivalentTo(new[]
		{
			"Root",
			"├─ a long item",
			"│  that wraps",
			"└─ b",
		});
	}

	[Test]
	public async Task Tree_Linear_IndentsInsteadOfDrawing()
		=> await Assert.That(Lines(Channels(TreeGuide.Line), 30, new BlockRenderOptions { Linear = true })).IsEquivalentTo(new[]
		{
			"Channels",
			"  Public",
			"    +chat",
			"    +ooc",
			"  Staff",
			"    +admin",
		});

	[Test]
	public async Task Tree_InHtml_IsANestedList()
	{
		var html = BlockLayout.Build(Channels(TreeGuide.Rounded), 30).Render(MarkupFormat.Html, Registry);

		await Assert.That(html).Contains("<ul class=\"ms-tree ms-guide-rounded\"><li><div class=\"ms-text\">Channels</div><ul><li><div class=\"ms-text\">Public</div><ul><li>");
		await Assert.That(html).DoesNotContain("├");
	}

	[Test]
	public async Task FieldsAndTree_SurviveTheSerializer()
	{
		var node = new StackNode([Fields(FieldsOptions.Default with { Leader = P("."), Columns = 2, LabelAlignment = Alignment.Right }, Sheet), Channels(TreeGuide.Heavy with { Last = P("┗▶ ") })]);
		var text = BlockLayout.Build(node, 60);

		var json = MarkupTextSerializer.Serialize(text, Registry);
		var read = MarkupTextSerializer.Deserialize(json, Registry);

		await Assert.That(read.Render(MarkupFormat.Html, Registry)).IsEqualTo(text.Render(MarkupFormat.Html, Registry));
		await Assert.That(BlockLayout.Relayout(read, 0, new BlockRenderOptions { AsciiOnly = true }).ToPlainText()).Contains("`- +admin");
		await Assert.That(MarkupTextSerializer.Serialize(read, Registry)).IsEqualTo(json);
	}
}
