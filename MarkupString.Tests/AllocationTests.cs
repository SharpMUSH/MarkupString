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
