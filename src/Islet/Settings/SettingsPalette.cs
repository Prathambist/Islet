using System.Windows.Media;

namespace Islet
{
    /// <summary>
    /// The colours that follow from a palette being dark or light: text, dividers, hover tints
    /// and the neutral parts of controls. Dark palettes use white text on dark; light ones the reverse.
    /// </summary>
    public sealed record SettingsInk(
        Color Text,
        Color TextSecondary,
        Color TextFaint,
        Color Divider,
        Color Hover,
        Color Control,
        Color ControlSelected,
        Color NavSelected,
        Color SwitchOff,
        Color Pill,
        Color Thumb,
        Color ThumbHover)
    {
        static Color White(byte alpha) => Color.FromArgb(alpha, 255, 255, 255);
        static Color Black(byte alpha) => Color.FromArgb(alpha, 0, 0, 0);
        static Color Rgb(int hex) => Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);

        public static readonly SettingsInk OnDark = new(
            White(255), White(140), White(102), White(14), White(10),
            Rgb(0x262629), Rgb(0x48484A), White(31), Rgb(0x3A3A3C), Rgb(0x2C2C2E), White(48), White(112));

        public static readonly SettingsInk OnLight = new(
            Rgb(0x1C1C1E), Rgb(0x6E6E73), Rgb(0x9A9AA0), Black(20), Black(10),
            Rgb(0xE8E8ED), Rgb(0xFFFFFF), Black(18), Rgb(0xD1D1D6), Rgb(0xE5E5EA), Black(51), Black(110));
    }

    /// <summary>
    /// A colour scheme for the Settings window: its background, sidebar, cards, and the accent
    /// used for switches, the sidebar's marker, focus rings and highlights.
    /// </summary>
    public sealed record SettingsPalette(string Name, Color Background, Color Sidebar, Color Card, Color Accent, Color SwitchOn, bool Light = false)
    {
        public const string AlbumName = "Album";

        public SettingsInk Ink => Light ? SettingsInk.OnLight : SettingsInk.OnDark;

        /// <summary>A soft wash of the accent, behind whatever is selected.</summary>
        public Color AccentSoft => Color.FromArgb((byte)(Light ? 34 : 48), Accent.R, Accent.G, Accent.B);

        /// <summary>The accent as text on that wash: lighter on dark palettes, deeper on light ones, so it reads.</summary>
        public Color AccentText => AlbumPalette.Tone(Accent, Light ? 0.36 : 0.74, 0.5, 1);

        static Color Rgb(int hex) => Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);

        public static readonly SettingsPalette Graphite = new("Graphite",
            Rgb(0x0B0B0D), Rgb(0x121214), Rgb(0x1A1A1D), Rgb(0x0A84FF), Rgb(0x30D158));

        /// <summary>The fixed dark palettes, in the order their dots are shown; Album follows them.</summary>
        public static readonly IReadOnlyList<SettingsPalette> Dark = new[]
        {
            Graphite,
            new SettingsPalette("Midnight", Rgb(0x0A0F1C), Rgb(0x0E1527), Rgb(0x141D33), Rgb(0x5E9EFF), Rgb(0x5E9EFF)),
            new SettingsPalette("Aurora", Rgb(0x0A1013), Rgb(0x0E161A), Rgb(0x142026), Rgb(0x2DD4BF), Rgb(0x2DD4BF)),
            new SettingsPalette("Rosé", Rgb(0x120C10), Rgb(0x191117), Rgb(0x22171F), Rgb(0xFF7AA2), Rgb(0xFF7AA2)),
            new SettingsPalette("Ember", Rgb(0x120E0A), Rgb(0x19140E), Rgb(0x231C14), Rgb(0xFFB340), Rgb(0xFFB340))
        };

        /// <summary>
        /// Light palettes: soft tinted backgrounds with white cards, and accents deep enough to
        /// read clearly on white.
        /// </summary>
        public static readonly IReadOnlyList<SettingsPalette> LightOnes = new[]
        {
            new SettingsPalette("Paper", Rgb(0xF5F5F7), Rgb(0xECECEF), Rgb(0xFFFFFF), Rgb(0x0A7AFF), Rgb(0x34C759), true),
            new SettingsPalette("Sky", Rgb(0xEEF3FA), Rgb(0xE2EAF6), Rgb(0xFFFFFF), Rgb(0x2F6FEB), Rgb(0x2F6FEB), true),
            new SettingsPalette("Mint", Rgb(0xEDF6F2), Rgb(0xE0EFE8), Rgb(0xFFFFFF), Rgb(0x0F9D77), Rgb(0x0F9D77), true),
            new SettingsPalette("Blush", Rgb(0xFBF0F3), Rgb(0xF4E2E8), Rgb(0xFFFFFF), Rgb(0xD9466F), Rgb(0xD9466F), true),
            new SettingsPalette("Sand", Rgb(0xF7F1E7), Rgb(0xEEE5D6), Rgb(0xFFFDF8), Rgb(0xC46A12), Rgb(0xD9822B), true)
        };

        /// <summary>Every fixed palette, dark then light.</summary>
        public static readonly IReadOnlyList<SettingsPalette> Fixed = Dark.Concat(LightOnes).ToList();

        /// <summary>
        /// Taken from the song that's playing: its colour as the accent, and a very dark shade
        /// of it behind everything, so it reads as a hint of the cover rather than a paint job.
        /// </summary>
        public static SettingsPalette Album(Color cover)
        {
            var accent = AlbumPalette.Tone(cover, 0.64, 0.55, 0.9);

            return new SettingsPalette(AlbumName,
                AlbumPalette.Tone(cover, 0.045, 0.2, 0.4),
                AlbumPalette.Tone(cover, 0.065, 0.2, 0.4),
                AlbumPalette.Tone(cover, 0.095, 0.2, 0.4),
                accent,
                accent);
        }

        /// <summary>The saved choice, falling back to Graphite for anything unknown.</summary>
        public static SettingsPalette Find(string? name, Color cover) =>
            name == AlbumName ? Album(cover) : Fixed.FirstOrDefault(p => p.Name == name) ?? Graphite;
    }
}
