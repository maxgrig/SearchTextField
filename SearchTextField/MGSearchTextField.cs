using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CoreGraphics;
using Foundation;
using UIKit;

namespace SearchTextField
{
    public delegate void SearchTextFieldItemHandler(List<SearchTextFieldItem> filteredResults, int index);

    [Register("MGSearchTextField"), DesignTimeVisible(true)]
    public class MGSearchTextField : UITextField, IUITableViewDelegate, IUITableViewDataSource
    {
        public MGSearchTextField(IntPtr handle) : base(handle) { }

        /// Maximum number of results to be shown in the suggestions list
        public nint maxNumberOfResults = 0;

        /// Maximum height of the results list
        public nint maxResultsListHeight = 0;

        /// Indicate if this field has been interacted with yet
        public bool interactedWith = false;

        /// Indicate if keyboard is showing or not
        public bool keyboardIsShowing = false;

        /// How long to wait before deciding typing has stopped
        public double typingStoppedDelay = 0.8;

        /// Set your custom visual theme, or just choose between pre-defined SearchTextFieldTheme.lightTheme() and SearchTextFieldTheme.darkTheme() themes
        SearchTextFieldTheme _theme = SearchTextFieldTheme.lightTheme();
        public SearchTextFieldTheme theme
        {
            get => _theme;
            set
            {
                _theme = value;

                tableView?.ReloadData();

                var placeholderColor = theme.placeholderColor;
                if (placeholderColor != null)
                {
                    var placeholderString = Placeholder;
                    if (!string.IsNullOrWhiteSpace(placeholderString))
                    {
                        this.AttributedPlaceholder = new NSAttributedString(str: placeholderString, attributes: new UIStringAttributes { ForegroundColor = placeholderColor });
                    }

                    this.placeholderLabel.TextColor = placeholderColor;
                }

                var hightlightedFont = this.highlightAttributes.Font as UIFont;
                if (hightlightedFont != null)
                {
                    this.highlightAttributes.Font = hightlightedFont.WithSize(this.theme.font.PointSize);
                }
            }
        }

        /// Show the suggestions list without filter when the text field is focused
        public bool startVisible = false;

        /// Show the suggestions list without filter even if the text field is not focused
        bool _startVisibleWithoutInteraction = false;
        public bool startVisibleWithoutInteraction
        {
            get => _startVisibleWithoutInteraction;
            set
            {
                _startVisibleWithoutInteraction = value;
                if (startVisibleWithoutInteraction)
                {
                    textFieldDidChange();
                }
            }
        }

        /// Set an array of SearchTextFieldItem's to be used for suggestions
        public void filterItems(List<SearchTextFieldItem> items)
        {
            filterDataSource = items;
        }

        /// Set an array of strings to be used for suggestions
        public void filterStrings(List<string> strings)
        {
            var items = new List<SearchTextFieldItem>();

            foreach (var value in strings)
            {
                items.Add(new SearchTextFieldItem(title: value));
            }

            filterItems(items);
        }

        /// Closure to handle when the user pick an item
        public SearchTextFieldItemHandler itemSelectionHandler;

        /// Closure to handle when the user stops typing
        public Action userStoppedTypingHandler;

        /// Set your custom set of attributes in order to highlight the string found in each item
        public UIStringAttributes highlightAttributes { get; } = new UIStringAttributes { Font = UIFont.BoldSystemFontOfSize(14) };

        /// Start showing the default loading indicator, useful for searches that take some time.
        public void showLoadingIndicator()
        {
            this.RightViewMode = UITextFieldViewMode.Always;
            indicator.StartAnimating();
        }

        /// Force the results list to adapt to RTL languages
        public bool forceRightToLeft = false;

        /// Hide the default loading indicator
        public void stopLoadingIndicator()
        {
            this.RightViewMode = UITextFieldViewMode.Never;
            indicator.StopAnimating();
        }

        /// When InlineMode is true, the suggestions appear in the same line than the entered string. It's useful for email domains suggestion for example.
        private bool _inlineMode = false;
        public bool inlineMode
        {
            get => _inlineMode;
            set
            {
                _inlineMode = value;
                if (_inlineMode == true)
                {
                    AutocorrectionType = UITextAutocorrectionType.No;
                    SpellCheckingType = UITextSpellCheckingType.No;
                }
            }
        }

        /// Only valid when InlineMode is true. The suggestions appear after typing the provided string (or even better a character like '@')
        public String startFilteringAfter;

        /// Min number of characters to start filtering
        public nint minCharactersNumberToStartFiltering;

        /// Force no filtering (display the entire filtered data source)
        public bool forceNoFiltering = false;

        /// If startFilteringAfter is set, and startSuggestingInmediately is true, the list of suggestions appear inmediately
        public bool startSuggestingInmediately = false;

        /// Allow to decide the comparision options
        public StringComparison comparisonOptions = StringComparison.InvariantCultureIgnoreCase;

        /// Set the results list's header
        public UIView resultsListHeader;

        // Move the table around to customize for your layout
        public nfloat tableXOffset = 0.0f;
        public nfloat tableYOffset = 0.0f;
        public nfloat tableCornerRadius = 2.0f;
        public nfloat tableBottomMargin = 10.0f;

        ////////////////////////////////////////////////////////////////////////
        // Private implementation

        private UITableView tableView;
        private UIView shadowView;
        private Direction direction = Direction.down;
        private nfloat fontConversionRate = 0.7f;
        private CGRect? keyboardFrame;
        private NSTimer timer;
        private UILabel placeholderLabel;
        private static string cellIdentifier = "APSearchTextFieldCell";
        private UIActivityIndicatorView indicator = new UIActivityIndicatorView(style: UIActivityIndicatorViewStyle.Gray);
        private nfloat maxTableViewSize = 0f;

        private List<SearchTextFieldItem> filteredResults = new List<SearchTextFieldItem>();

        private List<SearchTextFieldItem> _filterDataSource = new List<SearchTextFieldItem>();
        private List<SearchTextFieldItem> filterDataSource
        {
            get => _filterDataSource;
            set
            {
                _filterDataSource = value;

                filter(forceShowAll: forceNoFiltering);
                buildSearchTableView();

                if (startVisibleWithoutInteraction)
                {
                    textFieldDidChange();
                }
            }
        }

        private string currentInlineItem = "";

        public override void WillMoveToWindow(UIWindow window)
        {
            base.WillMoveToWindow(window);
            tableView?.RemoveFromSuperview();
        }

        public override void WillMoveToSuperview(UIView newsuper)
        {
            base.WillMoveToSuperview(newsuper);

            this.EditingChanged += (sender, e) => textFieldDidChange();
            this.EditingDidBegin += (sender, e) => textFieldDidBeginEditing();
            this.EditingDidEnd += (sender, e) => textFieldDidEndEditing();
            this.EditingDidEndOnExit += (sender, e) => textFieldDidEndEditingOnExit();

            UIKeyboard.Notifications.ObserveWillShow(KeyboardWillShow);
            UIKeyboard.Notifications.ObserveWillHide(KeyboardWillHide);
            UIKeyboard.Notifications.ObserveDidChangeFrame(KeyboardDidChangeFrame);
        }

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();

            if (inlineMode)
            {
                buildPlaceholderLabel();
            }
            else
            {
                buildSearchTableView();
            }

            // Create the loading indicator
            indicator.HidesWhenStopped = true;
            this.RightView = indicator;
        }

        public override CGRect RightViewRect(CGRect forBounds)
        {
            var rightFrame = base.RightViewRect(forBounds: forBounds);
            rightFrame.X -= 5;
            return rightFrame;
        }

        // Create the filter table and shadow view
        private void buildSearchTableView()
        {
            if (tableView != null && shadowView != null)
            {
                tableView.Layer.MasksToBounds = true;
                tableView.Layer.BorderWidth = theme.borderWidth > 0 ? theme.borderWidth : 0.5f;
                tableView.WeakDataSource = this;
                tableView.WeakDelegate = this;
                tableView.SeparatorInset = UIEdgeInsets.Zero;
                tableView.TableHeaderView = resultsListHeader;
                if (forceRightToLeft)
                {
                    tableView.SemanticContentAttribute = UISemanticContentAttribute.ForceRightToLeft;
                }

                shadowView.BackgroundColor = UIColor.LightTextColor;
                shadowView.Layer.ShadowColor = UIColor.Black.CGColor;
                shadowView.Layer.ShadowOffset = CGSize.Empty;
                shadowView.Layer.ShadowOpacity = 1;

                this.Window?.AddSubview(tableView);
            }
            else
            {
                tableView = new UITableView(frame: CGRect.Empty);
                shadowView = new UIView(frame: CGRect.Empty);
            }

            redrawSearchTableView();
        }

        private void buildPlaceholderLabel()
        {
            var newRect = this.PlaceholderRect(forBounds: this.Bounds);
            var caretRect = this.GetCaretRectForPosition(this.BeginningOfDocument);
            var textRect = this.TextRect(forBounds: this.Bounds);

            var range = GetTextRange(BeginningOfDocument, EndOfDocument);
            if (range != null)
            {
                caretRect = this.GetFirstRectForRange(range);
            }

            newRect.X = caretRect.X + caretRect.Size.Width + textRect.X;
            newRect.Width = newRect.Width - newRect.X;

            if (placeholderLabel != null)
            {
                placeholderLabel.Font = this.Font;
                placeholderLabel.Frame = newRect;
            }
            else
            {
                placeholderLabel = new UILabel(frame: newRect);
                placeholderLabel.Font = this.Font;
                placeholderLabel.BackgroundColor = UIColor.Clear;
                placeholderLabel.LineBreakMode = UILineBreakMode.Clip;

                var placeholderColor = this.AttributedPlaceholder?.GetAttribute(UIStringAttributeKey.ForegroundColor, 0, out NSRange effectiveRange) as UIColor;
                if (placeholderColor != null)
                {
                    placeholderLabel.TextColor = placeholderColor;
                }
                else
                {
                    placeholderLabel.TextColor = UIColor.FromRGBA(red: 0.8f, green: 0.8f, blue: 0.8f, alpha: 1.0f);
                }

                this.AddSubview(placeholderLabel);
            }
        }

        private void redrawSearchTableView()
        {
            if (inlineMode)
            {
                tableView.Hidden = true;
                return;
            }

            if (tableView != null)
            {
                var frameNullable = this.Superview?.ConvertRectToView(this.Frame, null);
                if (!frameNullable.HasValue)
                {
                    return;
                }
                var frame = frameNullable.Value;

                //  TableViews use estimated cell heights to calculate content size until they
                //  are on-screen. We must set this to the theme cell height to avoid getting an
                //  incorrect contentSize when we have specified non-standard fonts and/or
                //  cellHeights in the theme. We do it here to ensure updates to these settings
                //  are recognized if changed after the tableView is created
                tableView.EstimatedRowHeight = theme.cellHeight;

                if (this.direction == Direction.down)
                {
                    nfloat tableHeight = 0f;

                    var keyboardHeight = keyboardFrame?.Height;
                    if (keyboardIsShowing && keyboardHeight != null)
                    {
                        tableHeight = (nfloat)Math.Min(tableView.ContentSize.Height, UIScreen.MainScreen.Bounds.Height - frame.Y - frame.Height - (nfloat)keyboardHeight);
                    }
                    else
                    {
                        tableHeight = (nfloat)Math.Min(tableView.ContentSize.Height, UIScreen.MainScreen.Bounds.Height - frame.Y - frame.Height);
                    }

                    if (maxResultsListHeight > 0)
                    {
                        tableHeight = (nfloat)Math.Min(tableHeight, maxResultsListHeight);
                    }

                    // Set a bottom margin of 10p
                    if (tableHeight < tableView.ContentSize.Height)
                    {
                        tableHeight -= tableBottomMargin;
                    }

                    var tableViewFrame = new CGRect(x: 0, y: 0, width: frame.Width - 4, height: tableHeight);
                    var origin = this.ConvertRectToView(tableViewFrame, null);
                    tableViewFrame.X = origin.X + 2 + tableXOffset;
                    tableViewFrame.Y = origin.Y + frame.Height + 2 + tableYOffset;
                    UIView.Animate(0.2, () => {
                        tableView.Frame = tableViewFrame;
                    });

                    var shadowFrame = new CGRect(x: 0, y: 0, width: frame.Width - 6, height: 1);
                    origin = this.ConvertRectToView(shadowFrame, null);
                    shadowFrame.X = origin.X + 3;
                    shadowFrame.Y = tableView.Frame.Y;
                    shadowView.Frame = shadowFrame;
                }
                else
                {
                    var tableHeight = (nfloat)Math.Min((tableView.ContentSize.Height), (UIScreen.MainScreen.Bounds.Height - frame.Y - theme.cellHeight));
                    UIView.Animate(0.2, () =>
                    {
                        this.tableView.Frame = new CGRect(x: frame.X + 2, y: (frame.Y - tableHeight), width: frame.Width - 4, height: tableHeight);
                        this.shadowView.Frame = new CGRect(x: frame.X + 3, y: (frame.Y + 3), width: frame.Width - 6, height: 1);
                    });
                }

                Superview?.BringSubviewToFront(tableView);
                Superview?.BringSubviewToFront(shadowView);

                if (this.IsFirstResponder)
                {
                    Superview?.BringSubviewToFront(this);
                }

                tableView.Layer.BorderColor = theme.borderColor.CGColor;
                tableView.Layer.CornerRadius = tableCornerRadius;
                tableView.SeparatorColor = theme.separatorColor;
                tableView.BackgroundColor = theme.bgColor;

                tableView.ReloadData();
            }
        }

        private void KeyboardWillShow(object sender, UIKeyboardEventArgs e)
        {
            if (!keyboardIsShowing && IsEditing)
            {
                keyboardIsShowing = true;
                keyboardFrame = e.FrameEnd;
                interactedWith = true;
                prepareDrawTableResult();
            }
        }

        private void KeyboardWillHide(object sender, UIKeyboardEventArgs e)
        {
            if (keyboardIsShowing)
            {
                keyboardIsShowing = false;
                direction = Direction.down;
                redrawSearchTableView();
            }
        }

        private void KeyboardDidChangeFrame(object sender, UIKeyboardEventArgs e)
        {
            var frameEnd = e.FrameEnd;

            Task.Delay(100).ContinueWith(t => InvokeOnMainThread(() =>
            {
                keyboardFrame = frameEnd;
                prepareDrawTableResult();
            }));
        }

        public void typingDidStop()
        {
            this.userStoppedTypingHandler?.Invoke();
        }

        // Handle text field changes
        public void textFieldDidChange()
        {
            if (!inlineMode && tableView == null)
            {
                buildSearchTableView();
            }

            interactedWith = true;

            // Detect pauses while typing
            timer?.Invalidate();
            //Timer.scheduledTimer(timeInterval: typingStoppedDelay, target: self, selector: #selector(SearchTextField.typingDidStop), userInfo: self, repeats: false)
            timer = NSTimer.CreateScheduledTimer(interval: typingStoppedDelay, repeats: false, block: t => typingDidStop());

            if (string.IsNullOrWhiteSpace(Text))
            {
                clearResults();
                tableView?.ReloadData();
                if (startVisible || startVisibleWithoutInteraction)
                {
                    filter(forceShowAll: true);
                }
                if (placeholderLabel != null)
                {
                    placeholderLabel.Text = "";
                }
            }
            else
            {
                filter(forceShowAll: forceNoFiltering);
                prepareDrawTableResult();
            }

            buildPlaceholderLabel();
        }

        public void textFieldDidBeginEditing()
        {
            if ((startVisible || startVisibleWithoutInteraction) && string.IsNullOrWhiteSpace(Text))
            {
                clearResults();
                filter(forceShowAll: true);
            }
            if (placeholderLabel != null)
            {
                placeholderLabel.AttributedText = null;
            }
        }

        public void textFieldDidEndEditing()
        {
            clearResults();
            tableView?.ReloadData();
            if (placeholderLabel != null)
            {
                placeholderLabel.AttributedText = null;
            }
        }

        public void textFieldDidEndEditingOnExit()
        {
            var firstElement = filteredResults.FirstOrDefault();
            if (firstElement != null)
            {
                if (itemSelectionHandler != null)
                {
                    itemSelectionHandler(filteredResults, 0);
                }
                else
                {
                    if (inlineMode && string.IsNullOrEmpty(startFilteringAfter))
                    {
                        var stringElements = this.Text?.Split(startFilteringAfter);
                        Text = stringElements.FirstOrDefault() + startFilteringAfter + firstElement.title;
                    }
                    else
                    {
                        Text = firstElement.title;
                    }
                }
            }
        }

        public void hideResultsList()
        {
            var tableFrame = tableView?.Frame;
            if (tableFrame.HasValue)
            {
                var newFrame = new CGRect(x: tableFrame.Value.X, y: tableFrame.Value.Y, width: tableFrame.Value.Width, height: 0.0f);
                UIView.Animate(0.2, () =>
                {
                    tableView.Frame = newFrame;
                });
            }
        }

        private void filter(bool forceShowAll)
        {
            clearResults();

            if (Text.Length < minCharactersNumberToStartFiltering)
            {
                return;
            }

            for (int i = 0; i < filterDataSource.Count; i++)
            {
                var item = filterDataSource[i];

                if (!inlineMode)
                {
                    // Find text in title and subtitle
                    var titleFilterStart = item.title.IndexOf(Text, comparisonOptions);
                    var subtitleFilterStart = !string.IsNullOrEmpty(item?.subtitle) ? item.subtitle.IndexOf(Text, comparisonOptions) : -1;

                    if (titleFilterStart >= 0 || subtitleFilterStart >= 0 || forceShowAll)
                    {
                        item.attributedTitle = new NSMutableAttributedString(item.title);
                        item.attributedSubtitle = new NSMutableAttributedString(!string.IsNullOrEmpty(item.subtitle) ? item.subtitle : "");

                        item.attributedTitle.SetAttributes(highlightAttributes, new NSRange(titleFilterStart, Text.Length));

                        if (subtitleFilterStart >= 0)
                        {
                            item.attributedSubtitle.SetAttributes(highlightAttributesForSubtitle(), new NSRange(subtitleFilterStart, Text.Length));
                        }

                        filteredResults.Add(item);
                    }
                }
                else
                {
                    var textToFilter = Text.ToLower();

                    if (inlineMode && !string.IsNullOrEmpty(startFilteringAfter))
                    {
                        var suffixToFilter = textToFilter.Split(startFilteringAfter).LastOrDefault();
                        if (suffixToFilter != null
                            && (suffixToFilter != "" || startSuggestingInmediately == true)
                            && (textToFilter != suffixToFilter))
                        {
                            textToFilter = suffixToFilter;
                        }
                        else
                        {
                            placeholderLabel.Text = "";
                            return;
                        }
                    }

                    if (item.title.ToLower().IndexOf(textToFilter, comparisonOptions) == 0)
                    {
                        //var indexFrom = textToFilter.index(textToFilter.startIndex, offsetBy: textToFilter.count)
                        var itemSuffix = item.title.Substring(textToFilter.Length);

                        item.attributedTitle = new NSMutableAttributedString(itemSuffix);
                        filteredResults.Add(item);
                    }
                }
            }

            tableView?.ReloadData();

            if (inlineMode)
            {
                handleInlineFiltering();
            }
        }

        // Clean filtered results
        private void clearResults()
        {
            filteredResults.Clear();
            tableView?.RemoveFromSuperview();
        }

        private UIStringAttributes highlightAttributesForSubtitle()
        {
            var attr = new UIStringAttributes(highlightAttributes.Dictionary);

            var font = highlightAttributes?.Font;

            if (font != null)
            {
                attr.Font = UIFont.FromName(font.Name, font.PointSize * fontConversionRate);
            }

            return attr;
        }

        // Handle inline behaviour
        private void handleInlineFiltering()
        {
            if (Text != null)
            {
                if (Text == "")
                {
                    if (placeholderLabel != null)
                    {
                        placeholderLabel.AttributedText = null;
                    }
                }
                else
                {
                    var firstResult = filteredResults.FirstOrDefault();
                    if (firstResult != null)
                    {
                        if (placeholderLabel != null)
                        {
                            placeholderLabel.AttributedText = firstResult.attributedTitle;
                        }
                    }
                    else
                    {
                        if (placeholderLabel != null)
                        {
                            placeholderLabel.AttributedText = null;
                        }
                    }
                }
            }
        }

        // MARK: - Prepare for draw table result

        private void prepareDrawTableResult()
        {
            var frame = Superview?.ConvertRectToCoordinateSpace(Frame, UIApplication.SharedApplication.KeyWindow);
            if (frame == null)
            {
                return;
            }

            if (keyboardFrame.HasValue)
            {
                var newFrame = frame.Value;
                newFrame.Height += theme.cellHeight;

                if (keyboardFrame.Value.IntersectsWith(newFrame))
                {
                    direction = Direction.up;
                }
                else
                {
                    direction = Direction.down;
                }

                redrawSearchTableView();
            }
            else
            {
                if (Center.Y + theme.cellHeight > UIApplication.SharedApplication.KeyWindow.Frame.Height)
                {
                    direction = Direction.up;
                }
                else
                {
                    direction = Direction.down;
                }
            }
        }

        // IMPLEMENT IUITableViewDelegate, IUITableViewDataSource

        public nint RowsInSection(UITableView tableView, nint section)
        {
            tableView.Hidden = !interactedWith || (filteredResults.Count == 0);
            shadowView.Hidden = !interactedWith || (filteredResults.Count == 0);

            if (maxNumberOfResults > 0)
            {
                return (nint)Math.Min(filteredResults.Count, maxNumberOfResults);
            }
            else
            {
                return filteredResults.Count;
            }
        }

        public UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
        {
            var cell = tableView.DequeueReusableCell(MGSearchTextField.cellIdentifier);

            if (cell == null)
            {
                cell = new UITableViewCell(style: UITableViewCellStyle.Subtitle, reuseIdentifier: MGSearchTextField.cellIdentifier);
            }

            cell.BackgroundColor = UIColor.Clear;
            cell.LayoutMargins = UIEdgeInsets.Zero;
            cell.PreservesSuperviewLayoutMargins = false;
            cell.TextLabel.Font = theme.font;
            cell.DetailTextLabel.Font = UIFont.FromName(name: theme.font.Name, size: theme.font.PointSize * fontConversionRate);
            cell.TextLabel.TextColor = theme.fontColor;
            cell.DetailTextLabel.TextColor = theme.subtitleFontColor;

            cell.TextLabel.Text = filteredResults[(indexPath as NSIndexPath).Row].title;
            cell.DetailTextLabel.Text = filteredResults[(indexPath as NSIndexPath).Row].subtitle;
            cell.TextLabel.AttributedText = filteredResults[(indexPath as NSIndexPath).Row].attributedTitle;
            cell.DetailTextLabel.AttributedText = filteredResults[(indexPath as NSIndexPath).Row].attributedSubtitle;

            cell.ImageView.Image = filteredResults[(indexPath as NSIndexPath).Row].image;

            cell.SelectionStyle = UITableViewCellSelectionStyle.None;

            return cell;
        }

        [Export("tableView:heightForRowAtIndexPath:")]
        public nfloat GetHeightForRow(UITableView tableView, NSIndexPath indexPath)
        {
            return theme.cellHeight;
        }

        [Export("tableView:didSelectRowAtIndexPath:")]
        public void RowSelected(UITableView tableView, NSIndexPath indexPath)
        {
            if (itemSelectionHandler == null)
            {
                Text = filteredResults[(indexPath as NSIndexPath).Row].title;
            }
            else
            {
                var index = indexPath.Row;
                itemSelectionHandler(filteredResults, index);
            }

            clearResults();
        }

    }
}
