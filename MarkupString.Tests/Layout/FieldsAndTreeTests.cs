using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

public class FieldsAndTreeTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml();

	private static MarkupText P(string text) => MarkupText.Plain(text);

	private static string[] Lines(Block block, int width, LayoutContext? context = null) =>
		[.. BlockLayout.Lines(block, width, context).Select(line => line.ToPlainText().TrimEnd())];

	private static Fields Sheet(params (string Label, string Value)[] pairs) =>
		new([.. pairs.Select(pair => new Field(P(pair.Label), P(pair.Value)))]);

	private static readonly (string, string)[] Character =
		[("Sex", "Male"), ("Species", "Human"), ("Job", "Dark Warrior"), ("Origin", "Super Robot Wars AG")];

	[Test]
	public async Task Fields_LineTheValuesUp()
		=> await Assert.That(Lines(Sheet(Character), 40)).IsEquivalentTo(new[]
		{
			"Sex:     Male",
			"Species: Human",
			"Job:     Dark Warrior",
			"Origin:  Super Robot Wars AG",
		});

	[Test]
	public async Task Fields_RightAlignedLabels()
		=> await Assert.That(Lines(Sheet(Character[..2]) with { LabelAlignment = Alignment.Right }, 40)).IsEquivalentTo(new[]
		{
			"    Sex: Male",
			"Species: Human",
		});

	[Test]
	public async Task Fields_LeaderFillsToTheSeparator()
		=> await Assert.That(Lines(Sheet(Character[..2]) with { Leader = P(".") }, 40)).IsEquivalentTo(new[]
		{
			"Sex....: Male",
			"Species: Human",
		});

	/// <summary>A value too long for its column wraps under itself, not under the label.</summary>
	[Test]
	public async Task Fields_ValueWrapsWithAHangingIndent()
		=> await Assert.That(Lines(Sheet(("Quote", "Hooooo? said the machine child, laughing")), 24)).IsEquivalentTo(new[]
		{
			"Quote: Hooooo? said the",
			"       machine child,",
			"       laughing",
		});

	[Test]
	public async Task Fields_TooNarrow_PutEachLabelOverItsValue()
		=> await Assert.That(Lines(Sheet(("Species", "Human")), 14)).IsEquivalentTo(new[]
		{
			"Species:",
			"  Human",
		});

	[Test]
	public async Task Fields_DealtIntoColumns_DownFirst()
		=> await Assert.That(Lines(Sheet(Character) with { Columns = 2 }, 60)).IsEquivalentTo(new[]
		{
			"Sex:     Male" + new string(' ', 19) + "Job:    Dark Warrior",
			"Species: Human" + new string(' ', 18) + "Origin: Super Robot Wars AG",
		});

	[Test]
	public async Task Fields_ColumnsStackWhenNarrow()
		=> await Assert.That(Lines(Sheet(Character) with { Columns = 2 }, 30)).IsEquivalentTo(new[]
		{
			"Sex:     Male",
			"Species: Human",
			"Job:    Dark Warrior",
			"Origin: Super Robot Wars AG",
		});

	[Test]
	public async Task Fields_InHtml_AreADefinitionList()
	{
		var html = BlockLayout.Build(Sheet(Character[..1]), 30).Render(MarkupFormat.Html, Registry);

		await Assert.That(html).Contains("<dl class=\"ms-fields\"><div class=\"ms-field\"><dt>Sex:</dt><dd><div class=\"ms-text\">Male</div></dd></div></dl>");
	}

	[Test]
	public async Task Fields_Linear_ReadLabelThenValue()
		=> await Assert.That(Lines(Sheet(Character[..2]) with { Columns = 2 }, 40, new LayoutContext { Linear = true }))
			.IsEquivalentTo(new[] { "Sex: Male", "Species: Human" });

	private static Tree Channels(TreeGuide guide) => new(
	[
		new TreeItem(P("Channels"),
		[
			new TreeItem(P("Public"),
			[
				new TreeItem(P("+chat")),
				new TreeItem(P("+ooc")),
			]),
			new TreeItem(P("Staff"),
			[
				new TreeItem(P("+admin")),
			]),
		]),
	])
	{ Guide = guide };

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
		=> await Assert.That(Lines(Channels(TreeGuide.Rounded), 30, new LayoutContext { AsciiOnly = true })).IsEquivalentTo(new[]
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
		var tree = new Tree([new TreeItem(P("Root"), [new TreeItem(P("a long item that wraps")), new TreeItem(P("b"))])]);

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
		=> await Assert.That(Lines(Channels(TreeGuide.Line), 30, new LayoutContext { Linear = true })).IsEquivalentTo(new[]
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
		var node = new Stack([Sheet(Character) with { Leader = P("."), Columns = 2, LabelAlignment = Alignment.Right }, Channels(TreeGuide.Heavy with { Last = P("┗▶ ") })]);
		var text = BlockLayout.Build(node, 60);

		var json = MarkupTextSerializer.Serialize(text, Registry);
		var read = MarkupTextSerializer.Deserialize(json, Registry);

		await Assert.That(read.Render(MarkupFormat.Html, Registry)).IsEqualTo(text.Render(MarkupFormat.Html, Registry));
		await Assert.That(BlockLayout.Relayout(read, 0, new LayoutContext { AsciiOnly = true }).ToPlainText()).Contains("`- +admin");
		await Assert.That(MarkupTextSerializer.Serialize(read, Registry)).IsEqualTo(json);
	}
}
