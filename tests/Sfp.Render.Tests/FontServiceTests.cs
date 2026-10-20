using Sfp.Core.Manifests;
using Sfp.Render.Fonts;
using Sfp.TestSupport;
using SkiaSharp;

namespace Sfp.Render.Tests;

/// <summary>M2.2: the template typeface by family, the bundled fallback with its warning, and refusal of substitutions.</summary>
public class FontServiceTests
{
    private static readonly FontSpec Spec = TestManifest.Shipped.Font;
    private static readonly string TemplatesDir = TestPaths.TemplatesDir;
    private static readonly string BonumRegular = Path.Combine(TemplatesDir, "fonts", "texgyrebonum-regular.otf");

    [Fact]
    public void Font_directories_without_the_family_yield_the_fallback_and_a_warning()
    {
        string empty = TestPaths.NewTempDir("nofonts");
        using FontService fonts = FontService.Load(Spec, TemplatesDir, new[] { empty, Path.Combine(empty, "missing") });

        fonts.IsFallback.Should().BeTrue();
        fonts.RequestedFamily.Should().Be("Bookman Old Style");
        fonts.FamilyInUse.Should().Be(Spec.Fallback);
        fonts.Warnings.Should().ContainSingle()
            .Which.Should().Contain("Bookman Old Style").And.Contain(Spec.Fallback).And.Contain("differ slightly");
        fonts.Typeface().FamilyName.Should().Be("TeX Gyre Bonum");
        fonts.Typeface(FontStyleKind.Bold).IsBold.Should().BeTrue();
        fonts.Typeface(FontStyleKind.Italic).IsItalic.Should().BeTrue();
        fonts.Typeface(FontStyleKind.BoldItalic).IsBold.Should().BeTrue();
        fonts.Typeface(FontStyleKind.BoldItalic).IsItalic.Should().BeTrue();
    }

    [Fact]
    public void Font_directory_holding_the_family_yields_the_face_without_warnings()
    {
        string dir = FontFixtures.WriteRenamedFallbackFamily(TestPaths.NewTempDir("bookman"), Spec.Family);
        using FontService fonts = FontService.Load(Spec, TemplatesDir, new[] { dir });

        fonts.IsFallback.Should().BeFalse();
        fonts.FamilyInUse.Should().Be("Bookman Old Style");
        fonts.Warnings.Should().BeEmpty();
        fonts.Typeface().FamilyName.Should().Be("Bookman Old Style");
        fonts.Typeface(FontStyleKind.Bold).IsBold.Should().BeTrue();
        fonts.Typeface(FontStyleKind.Italic).IsItalic.Should().BeTrue();
        fonts.Typeface(FontStyleKind.BoldItalic).IsBold.Should().BeTrue();
    }

    [Fact]
    public void Resolver_stub_registering_a_face_under_the_family_name_yields_that_face()
    {
        string renamed = FontFixtures.WriteRenamedCopy(BonumRegular, Spec.Family, Path.Combine(TestPaths.NewTempDir("stub"), "bookman.otf"));
        StubResolver stub = new(family => string.Equals(family, Spec.Family, StringComparison.Ordinal) ? SKTypeface.FromFile(renamed) : null);

        using FontService fonts = FontService.Load(Spec, TemplatesDir, stub);

        fonts.IsFallback.Should().BeFalse();
        fonts.FamilyInUse.Should().Be("Bookman Old Style");
        fonts.Typeface().FamilyName.Should().Be("Bookman Old Style");
        stub.Requests.Should().Equal(FontStyleKind.Regular, FontStyleKind.Bold, FontStyleKind.Italic, FontStyleKind.BoldItalic);
    }

    [Fact]
    public void Silent_substitution_by_the_resolver_is_an_error_not_a_warning()
    {
        StubResolver stub = new(_ => SKTypeface.FromFile(BonumRegular));

        Action act = () => FontService.Load(Spec, TemplatesDir, stub);

        FontSubstitutionException ex = act.Should().Throw<FontSubstitutionException>().Which;
        ex.RequestedFamily.Should().Be("Bookman Old Style");
        ex.ActualFamily.Should().Be("TeX Gyre Bonum");
        ex.Message.Should().Contain("Bookman Old Style").And.Contain("TeX Gyre Bonum");
    }

    [Fact]
    public void Substitution_of_a_secondary_style_is_refused_too()
    {
        string renamed = FontFixtures.WriteRenamedCopy(BonumRegular, Spec.Family, Path.Combine(TestPaths.NewTempDir("stub2"), "bookman.otf"));
        StubResolver stub = new(_ => SKTypeface.FromFile(renamed), style => style == FontStyleKind.Bold ? SKTypeface.FromFile(BonumRegular) : null);

        Action act = () => FontService.Load(Spec, TemplatesDir, stub);

        act.Should().Throw<FontSubstitutionException>().Which.Message.Should().Contain("Bold");
    }

    [Fact]
    public void Missing_secondary_style_falls_back_to_regular_with_a_note()
    {
        string renamed = FontFixtures.WriteRenamedCopy(BonumRegular, Spec.Family, Path.Combine(TestPaths.NewTempDir("stub3"), "bookman.otf"));
        StubResolver stub = new(_ => null, style => style == FontStyleKind.Regular ? SKTypeface.FromFile(renamed) : null);

        using FontService fonts = FontService.Load(Spec, TemplatesDir, stub);

        fonts.Typeface(FontStyleKind.Bold).Should().BeSameAs(fonts.Typeface());
        fonts.Warnings.Should().HaveCount(3).And.AllSatisfy(w => w.Should().Contain("Regular face is used"));
    }

    [Fact]
    public void Font_is_created_at_pixel_size_with_deterministic_rasterization_settings()
    {
        using FontService fonts = Fallback();
        using SKFont font = fonts.Font(15, 220 / 72.0);

        font.Size.Should().BeApproximately(15f * 220f / 72f, 0.001f);
        font.Edging.Should().Be(SKFontEdging.Antialias);
        font.Subpixel.Should().BeTrue();
        font.Hinting.Should().Be(SKFontHinting.None);
        font.Typeface.FamilyName.Should().Be(fonts.FamilyInUse);

        using SKFont bold = fonts.Font(10, 1.0, FontStyleKind.Bold);
        bold.Typeface.IsBold.Should().BeTrue();
    }

    [Fact]
    public void Font_rejects_a_non_positive_size_or_scale()
    {
        using FontService fonts = Fallback();
        Action size = () => fonts.Font(0);
        Action scale = () => fonts.Font(10, 0);
        size.Should().Throw<ArgumentOutOfRangeException>();
        scale.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Digit_width_is_in_points_and_scales_with_the_size()
    {
        using FontService fonts = Fallback();
        double ten = fonts.DigitWidth(10);
        double twenty = fonts.DigitWidth(20);

        ten.Should().BeInRange(4, 8, "a 10 pt Bookman-style digit is a bit over half an em wide");
        twenty.Should().BeApproximately(2 * ten, 0.02);
        fonts.TextWidth("0", 10).Should().BeLessThanOrEqualTo(ten);
        fonts.TextWidth("13", 10).Should().BeApproximately(fonts.TextWidth("1", 10) + fonts.TextWidth("3", 10), 0.05);
    }

    [Fact]
    public void Missing_fallback_file_is_reported_with_its_path()
    {
        FontSpec broken = Spec with { FallbackFiles = new Dictionary<string, string> { ["regular"] = "fonts/not-there.otf" } };
        Action act = () => FontService.Load(broken, TemplatesDir, new StubResolver(_ => null));
        act.Should().Throw<FileNotFoundException>().WithMessage("*not-there.otf*");
    }

    [Fact]
    public void Fallback_file_from_another_family_than_the_manifest_names_is_refused()
    {
        FontSpec wrongName = Spec with { Fallback = "Bookman Clone" };
        Action act = () => FontService.Load(wrongName, TemplatesDir, new StubResolver(_ => null));
        act.Should().Throw<FontSubstitutionException>().Which.ActualFamily.Should().Be("TeX Gyre Bonum");
    }

    [Fact]
    public void Fallback_without_secondary_style_files_reuses_the_regular_face()
    {
        FontSpec regularOnly = Spec with { FallbackFiles = new Dictionary<string, string> { ["regular"] = Spec.FallbackFiles["regular"] } };
        using FontService fonts = FontService.Load(regularOnly, TemplatesDir, new StubResolver(_ => null));
        fonts.Typeface(FontStyleKind.Bold).Should().BeSameAs(fonts.Typeface());
    }

    [Fact]
    public void Disposed_service_refuses_typefaces()
    {
        FontService fonts = Fallback();
        fonts.Dispose();
        fonts.Dispose();
        Action act = () => fonts.Typeface();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Directory_resolver_lists_families_and_ignores_missing_folders()
    {
        DirectoryFontResolver resolver = new(new[] { Path.Combine(TemplatesDir, "fonts"), Path.Combine(TemplatesDir, "no-such-folder") });

        resolver.Families.Should().Equal("TeX Gyre Bonum");
        using SKTypeface? bold = resolver.Resolve("tex gyre bonum", FontStyleKind.Bold);
        bold.Should().NotBeNull();
        bold!.IsBold.Should().BeTrue();
        bold.IsItalic.Should().BeFalse();
        resolver.Resolve("Bookman Old Style", FontStyleKind.Regular).Should().BeNull();
    }

    [Fact]
    public void Style_of_a_typeface_is_read_from_its_weight_and_slant()
    {
        using SKTypeface regular = SKTypeface.FromFile(BonumRegular);
        using SKTypeface boldItalic = SKTypeface.FromFile(Path.Combine(TemplatesDir, "fonts", "texgyrebonum-bolditalic.otf"));
        FontStyleKindExtensions.Of(regular).Should().Be(FontStyleKind.Regular);
        FontStyleKindExtensions.Of(boldItalic).Should().Be(FontStyleKind.BoldItalic);
        FontStyleKind.BoldItalic.ManifestKey().Should().Be("boldItalic");
    }

    [Fact]
    public void Renamed_font_fixture_reports_the_new_family_and_keeps_its_glyphs()
    {
        string renamed = FontFixtures.WriteRenamedCopy(BonumRegular, "Bookman Old Style", Path.Combine(TestPaths.NewTempDir("rename"), "x.otf"));
        using SKTypeface face = SKTypeface.FromFile(renamed);
        using SKTypeface original = SKTypeface.FromFile(BonumRegular);
        face.FamilyName.Should().Be("Bookman Old Style");
        face.GlyphCount.Should().Be(original.GlyphCount);
        using SKFont a = new(face, 10);
        using SKFont b = new(original, 10);
        a.MeasureText("0123456789").Should().Be(b.MeasureText("0123456789"));
    }

    private static FontService Fallback() => FontService.Load(Spec, TemplatesDir, new StubResolver(_ => null));

    /// <summary>A resolver whose answer is scripted per family and, optionally, per style.</summary>
    private sealed class StubResolver : IFontResolver
    {
        private readonly Func<string, SKTypeface?> byFamily;
        private readonly Func<FontStyleKind, SKTypeface?>? byStyle;

        public StubResolver(Func<string, SKTypeface?> byFamily, Func<FontStyleKind, SKTypeface?>? byStyle = null)
        {
            this.byFamily = byFamily;
            this.byStyle = byStyle;
        }

        public List<FontStyleKind> Requests { get; } = [];

        public SKTypeface? Resolve(string family, FontStyleKind style)
        {
            Requests.Add(style);
            return byStyle?.Invoke(style) ?? byFamily(family);
        }
    }
}
