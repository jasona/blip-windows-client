using System.Globalization;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;

namespace Blip.Windows.Views;

/// <summary>
/// The TextBox presenter (see the TextBox template in App.axaml), fixed for colour emoji.
/// Avalonia keeps a fallback font running across whitespace, so in "😂 x" the space is shaped
/// by the emoji font, whose space is emoji-wide (Noto: 1.25 em). That leaves a hole after every
/// emoji while typing. Here each whitespace run that follows a fallback-font cluster gets its own
/// style span, which ends the fallback run so the UI font draws the space. Everything else is
/// Avalonia's own layout: the base result is used whenever there is nothing to fix, and the
/// rebuilt layout reproduces its preedit underline and selection colour.
/// </summary>
public class EmojiTextPresenter : TextPresenter
{
    protected override TextLayout CreateTextLayout()
    {
        var layout = base.CreateTextLayout();
        if (PasswordChar != default && !RevealPassword) return layout;

        var preedit = PreeditText;
        var text = string.IsNullOrEmpty(preedit) ? Text : (Text ?? "").Insert(Math.Clamp(CaretIndex, 0, Text?.Length ?? 0), preedit);
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        if (string.IsNullOrEmpty(text) || !FontManager.Current.TryGetGlyphTypeface(typeface, out var ui)) return layout;

        var gaps = SpacesAfterFallback(text, ui).ToList();
        if (gaps.Count == 0) return layout;

        // Avalonia's own override: preedit underline, else the selection foreground.
        (int Start, int End, IBrush? Fg, TextDecorationCollection? Deco)? styled = null;
        if (!string.IsNullOrEmpty(preedit))
            styled = (CaretIndex, CaretIndex + preedit.Length, Foreground, TextDecorations.Underline);
        else if (ShowSelectionHighlight && SelectionForegroundBrush is { } sel && SelectionStart != SelectionEnd)
            styled = (Math.Min(SelectionStart, SelectionEnd), Math.Max(SelectionStart, SelectionEnd), sel, null);

        TextRunProperties Props(IBrush? fg, TextDecorationCollection? deco) =>
            new GenericTextRunProperties(typeface, FontFeatures, FontSize, textDecorations: deco, foregroundBrush: fg);
        var styledProps = styled is { } s0 ? Props(s0.Fg, s0.Deco) : null;

        var cuts = new SortedSet<int> { 0, text.Length };
        foreach (var (start, end) in gaps) { cuts.Add(start); cuts.Add(end); }
        if (styled is { } s1) { cuts.Add(Math.Clamp(s1.Start, 0, text.Length)); cuts.Add(Math.Clamp(s1.End, 0, text.Length)); }

        var overrides = new List<ValueSpan<TextRunProperties>>();
        var points = cuts.ToArray();
        for (var i = 0; i + 1 < points.Length; i++)
        {
            int a = points[i], b = points[i + 1];
            var inStyled = styled is { } s && a >= s.Start && b <= s.End;
            var inGap = gaps.Any(g => a >= g.Start && b <= g.End);
            if (inGap)
                // A distinct brush instance keeps these properties unequal to their neighbours,
                // so Avalonia cannot merge the span back into the emoji run.
                overrides.Add(new(a, b - a, Props(Fresh(inStyled ? styled!.Value.Fg : Foreground), inStyled ? styled!.Value.Deco : null)));
            else if (inStyled)
                overrides.Add(new(a, b - a, styledProps!));
        }

        var rebuilt = new TextLayout(text, typeface, FontFeatures, FontSize, Foreground, TextAlignment, TextWrapping,
            maxWidth: layout.MaxWidth, maxHeight: layout.MaxHeight, textStyleOverrides: overrides,
            flowDirection: FlowDirection, lineHeight: LineHeight, letterSpacing: LetterSpacing);
        layout.Dispose();
        return rebuilt;
    }

    private static IBrush? Fresh(IBrush? brush) => brush is ISolidColorBrush b ? new ImmutableSolidColorBrush(b.Color, b.Opacity) : brush;

    /// <summary>[Start, End) of each whitespace run that directly follows a cluster the UI font cannot draw.</summary>
    internal static IEnumerable<(int Start, int End)> SpacesAfterFallback(string text, IGlyphTypeface ui)
    {
        var e = StringInfo.GetTextElementEnumerator(text);
        var afterFallback = false;
        int? gapStart = null;
        while (e.MoveNext())
        {
            var cluster = e.GetTextElement();
            var at = e.ElementIndex;
            if (char.IsWhiteSpace(cluster[0]) && cluster[0] is not ('\r' or '\n'))
            {
                if (afterFallback) gapStart ??= at;
                continue;
            }
            if (gapStart is { } g) yield return (g, at);
            gapStart = null;
            var first = char.ConvertToUtf32(cluster, 0);
            afterFallback = cluster[0] is not ('\r' or '\n') && (EmojiText.IsEmoji(cluster) || !ui.TryGetGlyph((uint)first, out _));
        }
        if (gapStart is { } last) yield return (last, text.Length);
    }
}
