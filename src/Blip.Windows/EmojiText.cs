using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Blip.Windows;

/// <summary>
/// Read-only text with emoji pinned to the colour emoji font. Font fallback alone is not enough:
/// a text font that happens to carry ❤ or a keycap digit draws it in monochrome, and its wide
/// spaces leak between emoji. So text is split into grapheme clusters, and every emoji cluster
/// becomes its own Run in the emoji font while everything else stays in the UI font.
/// Use <c>local:EmojiText.Text="{Binding …}"</c> on a TextBlock or SelectableTextBlock.
/// </summary>
public static class EmojiText
{
    public static readonly FontFamily EmojiFont = new(Fonts.Emoji);

    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("Text", typeof(EmojiText));

    static EmojiText() => TextProperty.Changed.AddClassHandler<TextBlock>((tb, e) => Apply(tb, e.NewValue as string));

    public static string? GetText(TextBlock tb) => tb.GetValue(TextProperty);
    public static void SetText(TextBlock tb, string? value) => tb.SetValue(TextProperty, value);

    private static void Apply(TextBlock tb, string? text)
    {
        var inlines = new InlineCollection();
        foreach (var (part, emoji) in Split(text ?? ""))
            inlines.Add(emoji ? new Run(part) { FontFamily = EmojiFont } : new Run(part));
        tb.Inlines = inlines;
    }

    /// <summary>Consecutive text and emoji segments; concatenated they are exactly the input.</summary>
    public static IEnumerable<(string Text, bool Emoji)> Split(string s)
    {
        var buf = new StringBuilder();
        var bufEmoji = false;
        var e = StringInfo.GetTextElementEnumerator(s);
        while (e.MoveNext())
        {
            var cluster = e.GetTextElement();
            var emoji = IsEmoji(cluster);
            if (buf.Length > 0 && emoji != bufEmoji)
            {
                yield return (buf.ToString(), bufEmoji);
                buf.Clear();
            }
            bufEmoji = emoji;
            buf.Append(cluster);
        }
        if (buf.Length > 0) yield return (buf.ToString(), bufEmoji);
    }

    /// <summary>
    /// One grapheme cluster: emoji when it asks for emoji presentation (VS16, keycap, ZWJ,
    /// skin tone, flag) or starts with a character that defaults to it. A text-style selector
    /// (VS15) always keeps it text. ©, ®, ™, arrows and bare digits stay text unless they carry VS16.
    /// </summary>
    public static bool IsEmoji(string cluster)
    {
        if (cluster.Length == 0) return false;
        var runes = cluster.EnumerateRunes().Select(r => r.Value).ToList();
        if (runes.Contains(0xFE0E)) return false;
        if (runes.Contains(0xFE0F) || runes.Contains(0x20E3)) return true;
        var first = runes[0];
        if (runes.Contains(0x200D) && runes.Any(IsPictographic)) return true;
        if (runes.Any(r => r is >= 0x1F3FB and <= 0x1F3FF)) return true;   // skin tones
        if (first is >= 0x1F1E6 and <= 0x1F1FF) return true;               // regional indicators (flags)
        return DefaultEmoji(first);
    }

    private static bool IsPictographic(int r) => r >= 0x1F000 || r is >= 0x2600 and <= 0x27BF;

    /// <summary>Emoji_Presentation=Yes (Unicode 15), plus ❤ which Messages always draws as an emoji.</summary>
    private static bool DefaultEmoji(int r) => r switch
    {
        0x2764 => true,
        0x231A or 0x231B or (>= 0x23E9 and <= 0x23EC) or 0x23F0 or 0x23F3 or 0x25FD or 0x25FE => true,
        0x2614 or 0x2615 or (>= 0x2648 and <= 0x2653) or 0x267F or 0x2693 or 0x26A1 or 0x26AA or 0x26AB => true,
        0x26BD or 0x26BE or 0x26C4 or 0x26C5 or 0x26CE or 0x26D4 or 0x26EA or 0x26F2 or 0x26F3 or 0x26F5 => true,
        0x26FA or 0x26FD or 0x2705 or 0x270A or 0x270B or 0x2728 or 0x274C or 0x274E or (>= 0x2753 and <= 0x2755) => true,
        0x2757 or (>= 0x2795 and <= 0x2797) or 0x27B0 or 0x27BF or 0x2B1B or 0x2B1C or 0x2B50 or 0x2B55 => true,
        0x1F004 or 0x1F0CF or 0x1F18E or (>= 0x1F191 and <= 0x1F19A) or 0x1F201 or 0x1F21A or 0x1F22F => true,
        (>= 0x1F232 and <= 0x1F236) or (>= 0x1F238 and <= 0x1F23A) or 0x1F250 or 0x1F251 => true,
        >= 0x1F300 and <= 0x1F64F => true,   // pictographs, emoticons
        >= 0x1F680 and <= 0x1F6FF => true,   // transport and map
        >= 0x1F7E0 and <= 0x1F7EB => true,   // coloured circles and squares
        0x1F7F0 => true,
        >= 0x1F90C and <= 0x1F9FF => true,   // supplemental symbols and pictographs
        >= 0x1FA70 and <= 0x1FAFF => true,   // symbols and pictographs extended-A
        _ => false,
    };
}
