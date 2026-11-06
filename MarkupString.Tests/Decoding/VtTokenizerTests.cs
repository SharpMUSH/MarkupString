using MarkupString.Decoding;

public class VtTokenizerTests
{
	private sealed class Recorder : IVtTokenSink
	{
		public List<string> Tokens { get; } = [];

		public void Print(ReadOnlySpan<char> text) => Add($"print:{text}");

		public void Execute(char control) => Add($"exec:{(int)control}");

		public void Csi(in CsiSequence sequence)
		{
			var parameters = new List<string>();
			for (var i = 0; i < sequence.Count; i++)
			{
				parameters.Add((sequence.IsSubparameter(i) ? ":" : "") + sequence.Parameters[i]);
			}

			Tokens.Add($"csi:{(sequence.Prefix == '\0' ? "" : sequence.Prefix.ToString())}{string.Join(",", parameters)}{sequence.Intermediates}{sequence.Final}");
		}

		public void Esc(in EscSequence sequence) => Tokens.Add($"esc:{sequence.Intermediates}{sequence.Final}");

		public void Osc(ReadOnlySpan<char> payload) => Tokens.Add($"osc:{payload}");

		public void Dcs(in CsiSequence header, ReadOnlySpan<char> data) => Tokens.Add($"dcs:{header.Final}:{data}");

		public void Apc(ReadOnlySpan<char> payload) => Tokens.Add($"apc:{payload}");

		public void Abandoned(VtSequenceKind kind) => Tokens.Add($"abandoned:{kind}");

		private void Add(string token)
		{
			// Print may arrive in pieces; join neighbours so tests read the text whole.
			if (token.StartsWith("print:", StringComparison.Ordinal) && Tokens.Count > 0 && Tokens[^1].StartsWith("print:", StringComparison.Ordinal))
			{
				Tokens[^1] += token["print:".Length..];
				return;
			}

			Tokens.Add(token);
		}
	}

	private static List<string> Tokens(params string[] chunks)
	{
		var tokenizer = new VtTokenizer(VtLimits.Default);
		var sink = new Recorder();
		foreach (var chunk in chunks) tokenizer.Feed(chunk, sink);
		return sink.Tokens;
	}

	[Test]
	public async Task Csi_WithParameters_IsOneToken()
	{
		await Assert.That(Tokens("a\e[1;31mb")).IsEquivalentTo(["print:a", "csi:1,31m", "print:b"]);
	}

	[Test]
	public async Task Csi_SplitAcrossFeeds_IsStillOneToken()
	{
		await Assert.That(Tokens("\e[3", "8;5;2", "08mx")).IsEquivalentTo(["csi:38,5,208m", "print:x"]);
	}

	[Test]
	public async Task Csi_ColonSubparameters_AreMarked()
	{
		await Assert.That(Tokens("\e[38:2::10:20:30m")).IsEquivalentTo(["csi:38,:2,:-1,:10,:20,:30m"]);
	}

	[Test]
	public async Task Csi_PrivatePrefix_IsKept()
	{
		await Assert.That(Tokens("\e[>q")).IsEquivalentTo(["csi:>q"]);
	}

	[Test]
	public async Task Osc_EndsAtBelOrSt()
	{
		await Assert.That(Tokens("\e]2;one\a\e]2;two\e\\")).IsEquivalentTo(["osc:2;one", "osc:2;two"]);
	}

	[Test]
	public async Task Apc_EndsAtSt()
	{
		await Assert.That(Tokens("\e_Ga=q;AAAA\e\\z")).IsEquivalentTo(["apc:Ga=q;AAAA", "print:z"]);
	}

	[Test]
	public async Task Dcs_CarriesItsData()
	{
		await Assert.That(Tokens("\eP0;1q#0~-\e\\")).IsEquivalentTo(["dcs:q:#0~-"]);
	}

	[Test]
	public async Task LineFeed_AbandonsAnOpenSequence_ThenEndsTheLine()
	{
		await Assert.That(Tokens("\e[31\nok")).IsEquivalentTo(["abandoned:Csi", "exec:10", "print:ok"]);
	}

	[Test]
	public async Task Cancel_AbandonsTheSequence()
	{
		await Assert.That(Tokens("\e[31\u0018ok")).IsEquivalentTo(["abandoned:Csi", "print:ok"]);
	}

	[Test]
	public async Task Esc_WithIntermediate()
	{
		await Assert.That(Tokens("\e(0q\e(B")).IsEquivalentTo(["esc:(0", "print:q", "esc:(B"]);
	}

	[Test]
	public async Task C0Controls_AreExecuted()
	{
		await Assert.That(Tokens("a\rb\bc\a")).IsEquivalentTo(["print:a", "exec:13", "print:b", "exec:8", "print:c", "exec:7"]);
	}
}
