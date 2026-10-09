/// <summary>
/// Bounds on what constructing a <see cref="MarkupText"/> costs. A caller that builds and discards
/// intermediates by the thousand — a parser, a formatter — renders almost none of them, so nothing
/// about a render may be paid for at construction.
/// </summary>
public class AllocationTests
{
	[Test]
	public async Task Plain_DoesNotAllocateRenderCaches()
	{
		const int iterations = 10_000;

		// Warm the JIT and any statics so their allocations land outside the measurement.
		for (var i = 0; i < 100; i++) GC.KeepAlive(MarkupText.Plain("hello"));

		// Per-thread, not GC.GetTotalAllocatedBytes: that counts the whole process, so any test
		// running in parallel would land its allocations inside this window and inflate the result.
		// Everything between the two reads is synchronous, so it stays on one thread.
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < iterations; i++) GC.KeepAlive(MarkupText.Plain("hello"));
		var perInstance = (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;

		// The upper bound leaves room for allocator variation while still failing if per-instance
		// render caches come back. The lower bound is not padding: it fails the test if the
		// measurement ever reads zero, which would let a broken probe pass vacuously.
		await Assert.That(perInstance).IsBetween(32, 300);
	}
}

public class AnsiAllocationTests
{
	/// <summary>
	/// ansi() parses its codes on every call. The codes are read in place, so the markup returned is the
	/// only allocation; this fails if the parse goes back to building a string per code.
	/// </summary>
	[Test]
	public async Task ParsingLetterCodes_AllocatesOnlyTheMarkup()
	{
		const int iterations = 10_000;
		for (var i = 0; i < 100; i++) GC.KeepAlive(MarkupString.Ansi.AnsiCodeParser.Parse("hr/b u"));

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < iterations; i++) GC.KeepAlive(MarkupString.Ansi.AnsiCodeParser.Parse("hr/b u"));
		var perParse = (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;

		await Assert.That(perParse).IsBetween(16, 120);
	}

	/// <summary>Wrapping plain text in a style seen before builds no candidate set to look up.</summary>
	[Test]
	public async Task WrappingPlainText_ReusesTheStylesSet()
	{
		var text = MarkupText.Plain("Hello World");
		var first = MarkupText.Wrap(MarkupString.Ansi.AnsiCodeParser.Parse("hr"), text);
		var second = MarkupText.Wrap(MarkupString.Ansi.AnsiCodeParser.Parse("hr"), text);

		await Assert.That(second.Runs[0].Markups).IsSameReferenceAs(first.Runs[0].Markups);
	}
}
