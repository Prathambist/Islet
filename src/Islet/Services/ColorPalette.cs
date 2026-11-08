using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace Islet
{
    /// <summary>
    /// The colours picked lately, newest first. Picking a colour that's already there moves it
    /// to the front instead of adding it twice. Kept in its own small file, like the shelf.
    /// </summary>
    public sealed class ColorPalette
    {
        public const int MaxColors = 12;

        readonly string path;
        readonly List<Color> colors = new();

        public ColorPalette(string folder)
        {
            path = Path.Combine(folder, "colors.json");
            Load();
        }

        public IReadOnlyList<Color> Colors => colors;

        public event Action? Changed;

        public void Add(Color color)
        {
            color.A = 255;
            colors.Remove(color);
            colors.Insert(0, color);

            if (colors.Count > MaxColors)
                colors.RemoveRange(MaxColors, colors.Count - MaxColors);

            SaveAndNotify();
        }

        public void Remove(Color color)
        {
            if (colors.Remove(color))
                SaveAndNotify();
        }

        public void Clear()
        {
            if (colors.Count == 0)
                return;

            colors.Clear();
            SaveAndNotify();
        }

        /// <summary>"#3A7BD5"</summary>
        public static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        /// <summary>"rgb(58, 123, 213)"</summary>
        public static string Rgb(Color color) => $"rgb({color.R}, {color.G}, {color.B})";

        /// <summary>"#3A7BD5" or "3a7bd5".</summary>
        public static bool TryParse(string text, out Color color)
        {
            color = default;
            text = text.Trim().TrimStart('#');

            if (text.Length != 6 || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
                return false;

            color = Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
            return true;
        }

        /// <summary>Whether white text and icons would be hard to see on it.</summary>
        public static bool IsLight(Color color) =>
            0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B > 170;

        void Load()
        {
            try
            {
                if (!File.Exists(path))
                    return;

                foreach (var hex in JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? new())
                {
                    if (TryParse(hex, out var color) && !colors.Contains(color) && colors.Count < MaxColors)
                        colors.Add(color);
                }
            }
            catch
            {
            }
        }

        void SaveAndNotify()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(colors.Select(Hex)));
            }
            catch
            {
            }

            Changed?.Invoke();
        }
    }
}
