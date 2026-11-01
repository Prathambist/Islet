namespace MusicIsland
{
    public enum LyricsStyle
    {
        None,
        Line,
        Stage
    }

    /// <summary>What each performance mode turns on. Everything mode-dependent reads from here.</summary>
    public sealed record PerformanceProfile(
        bool AnimatedBars,
        bool RealAudio,
        LyricsStyle Lyrics,
        bool SongPeek,
        bool SongFlair,
        bool SpinViews,
        bool Marquee,
        bool Glow,
        bool OpenStagger,
        int ContinuousFps,
        int AudioIntervalMs,
        int HoverDelayMs,
        int CollapseDelayMs,
        double SpringResponse,
        bool TrimMemory)
    {
        static readonly PerformanceProfile LowProfile = new(
            AnimatedBars: false, RealAudio: false, Lyrics: LyricsStyle.None,
            SongPeek: false, SongFlair: false, SpinViews: false, Marquee: false, Glow: false, OpenStagger: false,
            ContinuousFps: 30, AudioIntervalMs: 0, HoverDelayMs: 170, CollapseDelayMs: 220,
            SpringResponse: 0.85, TrimMemory: true);

        static readonly PerformanceProfile MediumProfile = new(
            AnimatedBars: true, RealAudio: true, Lyrics: LyricsStyle.Line,
            SongPeek: true, SongFlair: false, SpinViews: true, Marquee: true, Glow: true, OpenStagger: false,
            ContinuousFps: 30, AudioIntervalMs: 33, HoverDelayMs: 170, CollapseDelayMs: 220,
            SpringResponse: 1, TrimMemory: true);

        static readonly PerformanceProfile HighProfile = new(
            AnimatedBars: true, RealAudio: true, Lyrics: LyricsStyle.Stage,
            SongPeek: true, SongFlair: true, SpinViews: true, Marquee: true, Glow: true, OpenStagger: true,
            ContinuousFps: 0, AudioIntervalMs: 0, HoverDelayMs: 110, CollapseDelayMs: 160,
            SpringResponse: 0.85, TrimMemory: false);

        public static PerformanceProfile For(PerformanceMode mode) => mode switch
        {
            PerformanceMode.Low => LowProfile,
            PerformanceMode.High => HighProfile,
            _ => MediumProfile
        };
    }

    public static class LyricStageLayout
    {
        /// <summary>
        /// Top of each visible line, so the current one sits at <paramref name="anchor"/> and
        /// the rest stack around it. With no current line (-1), the first one sits at the anchor.
        /// </summary>
        public static double[] Tops(IReadOnlyList<double> heights, int current, double anchor, double gap)
        {
            var tops = new double[heights.Count];
            int pivot = Math.Max(current, 0);

            if (tops.Length == 0)
                return tops;

            pivot = Math.Min(pivot, tops.Length - 1);
            tops[pivot] = anchor;

            for (int i = pivot - 1; i >= 0; i--)
                tops[i] = tops[i + 1] - heights[i] - gap;

            for (int i = pivot + 1; i < tops.Length; i++)
                tops[i] = tops[i - 1] + heights[i - 1] + gap;

            return tops;
        }
    }
}
