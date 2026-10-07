using MarkupString.Ansi;

/// <summary>The terminals this package knows by name, and what a player's choice lets each be sent.</summary>
public class TerminalProfileTests
{
	[Test]
	[Arguments("kitty(0.35.2)", "kitty")]
	[Arguments("xterm-kitty", "kitty")]
	[Arguments("WezTerm 20240203-110809-5046fc22", "wezterm")]
	[Arguments("iTerm2 3.5.0", "iterm2")]
	[Arguments("ghostty 1.1.0", "ghostty")]
	[Arguments("XTerm(390)", "xterm")]
	[Arguments("foot(1.16.2)", "foot")]
	public async Task ATerminalIsKnownByWhatItReports(string reported, string id) =>
		await Assert.That(TerminalProfile.Identify(reported)?.Id).IsEqualTo(id);

	[Test]
	[Arguments("xterm-256color")]
	[Arguments("MUDLET")]
	[Arguments("")]
	[Arguments(null)]
	public async Task WhatNamesNoTerminalIsNone(string? reported) =>
		await Assert.That(TerminalProfile.Identify(reported)).IsNull();

	[Test]
	public async Task APlayerNamesATerminalByItsId()
	{
		await Assert.That(TerminalProfile.Find("Windows-Terminal")).IsSameReferenceAs(TerminalProfile.WindowsTerminal);
		await Assert.That(TerminalProfile.Find("nonesuch")).IsNull();
		await Assert.That(TerminalProfile.Known.Select(p => p.Id).Distinct().Count()).IsEqualTo(TerminalProfile.Known.Count);
	}

	[Test]
	public async Task ATerminalIsSentOnlyWhatThePlayerTurnedOnOfWhatItCanDo()
	{
		var options = AnsiOutputOptions.For(TerminalProfile.Kitty, TerminalFeatures.KittyGraphics | TerminalFeatures.Sixel);

		await Assert.That(options.Features).IsEqualTo(TerminalFeatures.KittyGraphics);
		await Assert.That(options.Terminal).IsSameReferenceAs(TerminalProfile.Kitty);
		await Assert.That(AnsiOutputOptions.For(TerminalProfile.Kitty, TerminalFeatures.None).Features).IsEqualTo(TerminalFeatures.None);
	}
}
