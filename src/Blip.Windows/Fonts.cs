using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace Blip.Windows;

/// <summary>
/// Font fallback picks the first font that has a glyph, so a text font that carries ❤ or
/// ▶ draws them in monochrome. The UI font is therefore "Blip Inter": Inter with every
/// emoji-capable codepoint removed (tools/make_text_font.py), bundled so it is the same on
/// every machine. Anything it lacks falls back to a colour emoji font, which Skia draws in
/// colour (COLR on Windows, CBDT on Linux). That covers the composer TextBox too, where
/// <see cref="EmojiText"/> cannot split runs.
/// </summary>
public static class Fonts
{
    public const string Ui = "fonts:Blip#Blip Inter";
    public const string Emoji = "Segoe UI Emoji, Noto Color Emoji, Apple Color Emoji";

    public static AppBuilder WithBlipFonts(this AppBuilder builder) => builder
        .ConfigureFonts(fm => fm.AddFontCollection(
            new EmbeddedFontCollection(new Uri("fonts:Blip"), new Uri("avares://Blip/Assets/Fonts"))))
        .With(new FontManagerOptions
        {
            DefaultFamilyName = Ui,
            FontFallbacks =
            [
                new FontFallback { FontFamily = new FontFamily("Segoe UI Emoji") },
                new FontFallback { FontFamily = new FontFamily("Noto Color Emoji") },
                new FontFallback { FontFamily = new FontFamily("Segoe UI Symbol") },
            ],
        });
}
