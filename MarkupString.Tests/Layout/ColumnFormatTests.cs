using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

public class ColumnFormatTests
{
	[Test]
	public async Task Defaults_AreTheNeutralOnes()
	{
		var format = new ColumnFormat { Width = 10 };

		await Assert.That(format.Wrap).IsEqualTo(WrapMode.None);
		await Assert.That(format.BreakSpace).IsEqualTo(BreakSpace.Drop);
		await Assert.That(format.Alignment).IsEqualTo(Alignment.Left);
		await Assert.That(format.FillPhase).IsEqualTo(FillPhase.Continuous);
		await Assert.That(format.BlankLineFill).IsEqualTo(BlankLineFill.Pattern);
		await Assert.That(format.Truncation).IsEqualTo(TruncationType.Truncate);
		await Assert.That(format.CutFrom).IsEqualTo(CutFrom.End);
		await Assert.That(format.WhenEmpty).IsEqualTo(WhenEmpty.None);
		await Assert.That(format.Fill.Text).IsEqualTo(" ");
		await Assert.That(format.FillRight).IsNull();
	}

	[Test]
	public async Task With_ComposesWithoutMutating()
	{
		var basis = new ColumnFormat { Width = 10 };

		var wrapped = basis with { Wrap = WrapMode.Word };

		await Assert.That(basis.Wrap).IsEqualTo(WrapMode.None);
		await Assert.That(wrapped.Wrap).IsEqualTo(WrapMode.Word);
		await Assert.That(wrapped.Width).IsEqualTo(10);
	}

	[Test]
	public async Task Indent_DefaultsToEveryLineButTheFirst()
	{
		var indent = new Indent(4);

		await Assert.That(indent.FromLine).IsEqualTo(1);
		await Assert.That(indent.Widen).IsEqualTo(0);
	}
}
