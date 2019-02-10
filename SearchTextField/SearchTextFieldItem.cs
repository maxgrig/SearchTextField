using System;
using Foundation;
using UIKit;

namespace SearchTextField
{
    /// <summary>
    /// Filter Item
    /// </summary>
    public class SearchTextFieldItem
    {
        internal NSMutableAttributedString AttributedTitle { get; set; }
        internal NSMutableAttributedString AttributedSubtitle { get; set; }

        // Public interface
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public UIImage Image { get; set; }

        public SearchTextFieldItem(string title, string subtitle, UIImage image)
        {
            this.Title = title;
            this.Subtitle = subtitle;
            this.Image = image;
        }

        public SearchTextFieldItem(string title, string subtitle)
        {
            this.Title = title;
            this.Subtitle = subtitle;
        }

        public SearchTextFieldItem(string title)
        {
            this.Title = title;
        }
    }
}
