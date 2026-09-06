using NovelToEink.Xtc;

namespace NovelToEink.XtcConverter
{
    /// <summary>
    /// Options for XTC conversion
    /// </summary>
    public class XtcOptions
    {
        /// <summary>
        /// Target device. The output resolution follows from this (X3: 528x792, X4 Pro: 480x800).
        /// </summary>
        public XteinkDevice Device { get; set; } = XteinkDevice.X4Pro;

        /// <summary>
        /// Target display resolution, derived from <see cref="Device"/>.
        /// </summary>
        public (int Width, int Height) Resolution => Device.GetResolution();

        /// <summary>
        /// Font file to use for rendering
        /// </summary>
        public string? FontFile { get; set; } = "";

        /// <summary>
        /// Font size to use
        /// </summary>
        public int FontSize { get; set; } = 36;

        /// <summary>
        /// Ruby font size to use
        /// </summary>
        public int RubyFontSize { get; set; } = 14;

        /// <summary>
        /// Line spacing
        /// </summary>
        public int LineSpacing { get; set; } = 56;

        /// <summary>
        /// Top margin
        /// </summary>
        public int MarginTop { get; set; } = 12;

        /// <summary>
        /// Bottom margin
        /// </summary>
        public int MarginBottom { get; set; } = 12;

        /// <summary>
        /// Right margin
        /// </summary>
        public int MarginRight { get; set; } = 12;

        /// <summary>
        /// Left margin
        /// </summary>
        public int MarginLeft { get; set; } = 12;

        /// <summary>
        /// Whether to enable vertical writing mode
        /// </summary>
        public bool EnableVerticalWriting { get; set; } = true;

        /// <summary>
        /// Whether to render ruby annotations
        /// </summary>
        public bool RenderRuby { get; set; } = true;

        /// <summary>
        /// Whether to include illustrations from the EPUB
        /// </summary>
        public bool IncludeImages { get; set; } = true;

        /// <summary>
        /// Apply Floyd-Steinberg dithering to illustrations
        /// </summary>
        public bool DitherImages { get; set; } = true;

        /// <summary>
        /// Binarization threshold for text pages
        /// </summary>
        public int TextThreshold { get; set; } = 200;

        /// <summary>Maps these options onto the renderer settings of the XTC library.</summary>
        public XtcRenderOptions ToRenderOptions() => new()
        {
            Device = Device,
            FontFile = FontFile,
            FontSize = FontSize,
            RubyFontSize = RubyFontSize,
            LineSpacing = LineSpacing,
            MarginTop = MarginTop,
            MarginBottom = MarginBottom,
            MarginRight = MarginRight,
            MarginLeft = MarginLeft,
            Vertical = EnableVerticalWriting,
            RenderRuby = RenderRuby,
            IncludeImages = IncludeImages,
            DitherImages = DitherImages,
            TextThreshold = TextThreshold,
        };
    }
}
