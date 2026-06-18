using FluentAssertions;
using Fonts;

namespace SouthBaySoccer.Client.Tests;

public class FontAwesomeGlyphsTests
{
    public static TheoryData<string> Glyphs =>
        new()
        {
            FontAwesomeGlyphs.Futbol,
            FontAwesomeGlyphs.WhatsApp,
            FontAwesomeGlyphs.ShieldHalved,
            FontAwesomeGlyphs.ArrowUpRightFromSquare,
        };

    // AUTH-7 / INV-13: every pictogram is a Font Awesome glyph from the BMP Private Use Area
    // (U+E000..U+F8FF), never a Unicode emoji. Emoji live outside the PUA and most are surrogate
    // pairs (length 2 in UTF-16), so a single PUA char proves both "Font Awesome" and "not emoji".
    [Theory]
    [MemberData(nameof(Glyphs))]
    public void Glyph_IsFontAwesomePrivateUseArea_AndNotEmoji(string glyph)
    {
        glyph.Should().HaveLength(1);

        int codePoint = glyph[0];
        codePoint.Should().BeInRange(0xE000, 0xF8FF);
    }
}
