using System;
using UIKit;

namespace SearchTextField
{
    /// <summary>
    /// Search Text Field Theme
    /// </summary>
    public struct SearchTextFieldTheme
    {
        public nfloat CellHeight { get; set; }
        public UIColor BackgroundColor { get; set; }
        public UIColor BorderColor { get; set; }
        public nfloat BorderWidth { get; set; }
        public UIColor SeparatorColor { get; set; }
        public UIFont Font { get; set; }
        public UIColor FontColor { get; set; }
        public UIColor SubtitleFontColor { get; set; }
        public UIColor PlaceholderColor { get; set; }

        public SearchTextFieldTheme(nfloat cellHeight, UIColor bgColor, UIColor borderColor, UIColor separatorColor, UIFont font, UIColor fontColor, UIColor subtitleFontColor = null)
        {
            this.CellHeight = cellHeight;
            this.BorderColor = borderColor;
            this.SeparatorColor = separatorColor;
            this.BackgroundColor = bgColor;
            this.Font = font;
            this.FontColor = fontColor;
            this.SubtitleFontColor = subtitleFontColor ?? fontColor;

            BorderWidth = 0;
            PlaceholderColor = UIColor.LightGray;
        }

        public static SearchTextFieldTheme LightTheme()
        {
            return new SearchTextFieldTheme(
                cellHeight: 30,
                bgColor: UIColor.FromRGBA(1, 1, 1, 0.9f),
                borderColor: UIColor.FromRGBA(red: 0.9f, green: 0.9f, blue: 0.9f, alpha: 1.0f),
                separatorColor: UIColor.Clear,
                font: UIFont.SystemFontOfSize(14),
                fontColor: UIColor.Black);
        }

        public static SearchTextFieldTheme DarkTheme()
        {
            return new SearchTextFieldTheme(
                cellHeight: 30,
                bgColor: UIColor.FromRGBA(red: 0.8f, green: 0.8f, blue: 0.8f, alpha: 0.6f),
                borderColor: UIColor.FromRGBA(red: 0.7f, green: 0.7f, blue: 0.7f, alpha: 1.0f),
                separatorColor: UIColor.Clear,
                font: UIFont.SystemFontOfSize(14),
                fontColor: UIColor.White);
        }
    }
}
