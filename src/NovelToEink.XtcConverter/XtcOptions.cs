using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NovelToEink.XtcConverter
{
    /// <summary>
    /// Options for XTC conversion
    /// </summary>
    public class XtcOptions
    {
        /// <summary>
        /// Target display resolution (default 480x800 for X4 Pro)
        /// </summary>
        public (int Width, int Height) Resolution { get; set; } = (480, 800);

        /// <summary>
        /// Font file to use for rendering
        /// </summary>
        public string FontFile { get; set; } = "";

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
        /// Whether to enable vertical writing mode
        /// </summary>
        public bool EnableVerticalWriting { get; set; } = true;

        /// <summary>
        /// Whether to enable text wrapping
        /// </summary>
        public bool EnableTextWrapping { get; set; } = true;
    }
}