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
        public MGSearchTextField(IntPtr handle) : base(handle) 
        { 
        }

        public MGSearchTextField(CGRect frame) : base(frame)
        {
        }

        /// <summary>
        /// Maximum number of results to be shown in the suggestions list
        /// </summary>
        public nint MaxNumberOfResults { get; set; } = 0;

        /// <summary>
        /// Maximum height of the results list
        /// </summary>
        public nint MaxResultsListHeight { get; set; } = 0;

        /// <summary>
        /// Indicate if this field has been interacted with yet
        /// </summary>
        public bool InteractedWith { get; set; } = false;

        /// <summary>
        /// Indicate if keyboard is showing or not
        /// </summary>
        public bool KeyboardIsShowing { get; set; } = false;

        /// <summary>
        /// How long to wait before deciding typing has stopped
        /// </summary>
        public double TypingStoppedDelay { get; set; } = 0.8;

        private SearchTextFieldTheme _theme = SearchTextFieldTheme.LightTheme();
        /// <summary>
        /// Set your custom visual theme, or just choose between pre-defined SearchTextFieldTheme.lightTheme() and SearchTextFieldTheme.darkTheme() themes
        /// </summary>
        public SearchTextFieldTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value;

                _tableView?.ReloadData();

                var placeholderColor = Theme.PlaceholderColor;
                if (placeholderColor != null)
                {
                    var placeholderString = Placeholder;
                    if (!string.IsNullOrWhiteSpace(placeholderString))
                    {
                        this.AttributedPlaceholder = new NSAttributedString(str: placeholderString, attributes: new UIStringAttributes { ForegroundColor = placeholderColor });
                    }

                    this._placeholderLabel.TextColor = placeholderColor;
                }

                var hightlightedFont = this.HighlightAttributes.Font as UIFont;
                if (hightlightedFont != null)
                {
                    this.HighlightAttributes.Font = hightlightedFont.WithSize(this.Theme.Font.PointSize);
                }
            }
        }

        /// <summary>
        /// Show the suggestions list without filter when the text field is focused
        /// </summary>
        public bool StartVisible { get; set; } = false;

        private bool _startVisibleWithoutInteraction;
        /// <summary>
        /// Show the suggestions list without filter even if the text field is not focused
        /// </summary>
        public bool StartVisibleWithoutInteraction
        {
            get => _startVisibleWithoutInteraction;
            set
            {
                _startVisibleWithoutInteraction = value;
                if (StartVisibleWithoutInteraction)
                {
                    TextFieldDidChange();
                }
            }
        }

        /// <summary>
        /// Set an array of SearchTextFieldItem's to be used for suggestions
        /// </summary>
        public void FilterItems(List<SearchTextFieldItem> items)
        {
            FilterDataSource = items;
        }

        /// <summary>
        /// Set an array of strings to be used for suggestions
        /// </summary>
        public void FilterStrings(List<string> strings)
        {
            strings = strings ?? new List<string>();

            var items = new List<SearchTextFieldItem>();

            foreach (var value in strings)
            {
                items.Add(new SearchTextFieldItem(title: value));
            }

            FilterItems(items);
        }

        /// <summary>
        /// Closure to handle when the user pick an item
        /// </summary>
        public SearchTextFieldItemHandler ItemSelectionHandler { get; set; }

        /// <summary>
        /// Closure to handle when the user stops typing
        /// </summary>
        public Action UserStoppedTypingHandler { get; set; }

        /// <summary>
        /// Set your custom set of attributes in order to highlight the string found in each item
        /// </summary>
        public UIStringAttributes HighlightAttributes { get; } = new UIStringAttributes { Font = UIFont.BoldSystemFontOfSize(14) };

        /// <summary>
        /// Start showing the default loading indicator, useful for searches that take some time.
        /// </summary>
        public void ShowLoadingIndicator()
        {
            RightViewMode = UITextFieldViewMode.Always;
            _indicator.StartAnimating();
        }

        /// <summary>
        /// Force the results list to adapt to RTL languages
        /// </summary>
        public bool ForceRightToLeft { get; set; } = false;

        /// <summary>
        /// Hide the default loading indicator
        /// </summary>
        public void StopLoadingIndicator()
        {
            RightViewMode = UITextFieldViewMode.Never;
            _indicator.StopAnimating();
        }

        private bool _inlineMode;
        /// <summary>
        /// When InlineMode is true, the suggestions appear in the same line than the entered string. It's useful for email domains suggestion for example.
        /// </summary>
        public bool InlineMode
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

        /// <summary>
        /// Only valid when InlineMode is true. The suggestions appear after typing the provided string (or even better a character like '@')
        /// </summary>
        public string StartFilteringAfter { get; set; }

        /// <summary>
        /// Min number of characters to start filtering
        /// </summary>
        public nint MinCharactersNumberToStartFiltering { get; set; }

        /// <summary>
        /// Force no filtering (display the entire filtered data source)
        /// </summary>
        public bool ForceNoFiltering { get; set; } = false;

        /// <summary>
        /// If startFilteringAfter is set, and startSuggestingInmediately is true, the list of suggestions appear inmediately
        /// </summary>
        public bool StartSuggestingInmediately { get; set; } = false;

        /// <summary>
        /// Allow to decide the comparision options
        /// </summary>
        public StringComparison ComparisonOptions { get; set; } = StringComparison.InvariantCultureIgnoreCase;

        /// <summary>
        /// Set the results list's header
        /// </summary>
        public UIView ResultsListHeader { get; set; }

        // Move the table around to customize for your layout
        public nfloat TableXOffset { get; set; } = 0.0f;
        public nfloat TableYOffset { get; set; } = 0.0f;
        public nfloat TableCornerRadius { get; set; } = 2.0f;
        public nfloat TableBottomMargin { get; set; } = 10.0f;

        ////////////////////////////////////////////////////////////////////////
        // Private implementation

        private UITableView _tableView;
        private UIView _shadowView;
        private Direction _direction = Direction.Down;
        private nfloat _fontConversionRate = 0.7f;
        private CGRect? _keyboardFrame;
        private NSTimer _timer;
        private UILabel _placeholderLabel;
        private const string CellIdentifier = "APSearchTextFieldCell";
        private UIActivityIndicatorView _indicator = new UIActivityIndicatorView(style: UIActivityIndicatorViewStyle.Gray);
        private nfloat _maxTableViewSize = 0f;

        private List<SearchTextFieldItem> _filteredResults = new List<SearchTextFieldItem>();

        private List<SearchTextFieldItem> _filterDataSource = new List<SearchTextFieldItem>();
        private List<SearchTextFieldItem> FilterDataSource
        {
            get => _filterDataSource;
            set
            {
                _filterDataSource = value;

                Filter(forceShowAll: ForceNoFiltering);
                BuildSearchTableView();

                if (StartVisibleWithoutInteraction)
                {
                    TextFieldDidChange();
                }
            }
        }

        //private readonly string currentInlineItem = "";

        private NSObject _notificationKeyboardWillShow;
        private NSObject _notificationKeyboardWillHide;
        private NSObject _notificationKeyboardDidChangeFrame;

        public override void WillMoveToWindow(UIWindow window)
        {
            base.WillMoveToWindow(window);
            _tableView?.RemoveFromSuperview();
        }

        public override void WillMoveToSuperview(UIView newsuper)
        {
            if (newsuper == null)
            {
                UnsubscribeFromEvents();
            }

            base.WillMoveToSuperview(newsuper);

            if (newsuper != null)
            {
                SubscribeToEvents();
            }
        }

        private void SubscribeToEvents()
        {
            EditingChanged += TextFieldDidChange;
            EditingDidBegin += TextFieldDidBeginEditing;
            EditingDidEnd += TextFieldDidEndEditing;
            EditingDidEndOnExit += TextFieldDidEndEditingOnExit;

            _notificationKeyboardWillShow = UIKeyboard.Notifications.ObserveWillShow(KeyboardWillShow);
            _notificationKeyboardWillHide = UIKeyboard.Notifications.ObserveWillHide(KeyboardWillHide);
            _notificationKeyboardDidChangeFrame = UIKeyboard.Notifications.ObserveDidChangeFrame(KeyboardDidChangeFrame);
        }

        private void UnsubscribeFromEvents()
        {
            EditingChanged -= TextFieldDidChange;
            EditingDidBegin -= TextFieldDidBeginEditing;
            EditingDidEnd -= TextFieldDidEndEditing;
            EditingDidEndOnExit -= TextFieldDidEndEditingOnExit;

            _notificationKeyboardWillShow?.Dispose();
            _notificationKeyboardWillHide?.Dispose();
            _notificationKeyboardDidChangeFrame?.Dispose();
        }

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();

            if (InlineMode)
            {
                BuildPlaceholderLabel();
            }
            else
            {
                BuildSearchTableView();
            }

            // Create the loading indicator
            _indicator.HidesWhenStopped = true;
            RightView = _indicator;
        }

        public override CGRect RightViewRect(CGRect forBounds)
        {
            var rightFrame = base.RightViewRect(forBounds: forBounds);
            rightFrame.X -= 5;
            return rightFrame;
        }

        // Create the filter table and shadow view
        private void BuildSearchTableView()
        {
            if (_tableView != null && _shadowView != null)
            {
                _tableView.Layer.MasksToBounds = true;
                _tableView.Layer.BorderWidth = Theme.BorderWidth > 0 ? Theme.BorderWidth : 0.5f;
                _tableView.WeakDataSource = this;
                _tableView.WeakDelegate = this;
                _tableView.SeparatorInset = UIEdgeInsets.Zero;
                _tableView.TableHeaderView = ResultsListHeader;

                if (ForceRightToLeft)
                {
                    _tableView.SemanticContentAttribute = UISemanticContentAttribute.ForceRightToLeft;
                }

                _shadowView.BackgroundColor = UIColor.LightTextColor;
                _shadowView.Layer.ShadowColor = UIColor.Black.CGColor;
                _shadowView.Layer.ShadowOffset = CGSize.Empty;
                _shadowView.Layer.ShadowOpacity = 1;

                Window?.AddSubview(_tableView);
            }
            else
            {
                _tableView = new UITableView(frame: CGRect.Empty);
                _shadowView = new UIView(frame: CGRect.Empty);
            }

            RedrawSearchTableView();
        }

        private void BuildPlaceholderLabel()
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

            if (_placeholderLabel != null)
            {
                _placeholderLabel.Font = this.Font;
                _placeholderLabel.Frame = newRect;
            }
            else
            {
                _placeholderLabel = new UILabel(frame: newRect)
                {
                    Font = this.Font,
                    BackgroundColor = UIColor.Clear,
                    LineBreakMode = UILineBreakMode.Clip
                };

                var placeholderColor = this.AttributedPlaceholder?.GetAttribute(UIStringAttributeKey.ForegroundColor, 0, out NSRange effectiveRange) as UIColor;
                if (placeholderColor != null)
                {
                    _placeholderLabel.TextColor = placeholderColor;
                }
                else
                {
                    _placeholderLabel.TextColor = UIColor.FromRGBA(red: 0.8f, green: 0.8f, blue: 0.8f, alpha: 1.0f);
                }

                this.AddSubview(_placeholderLabel);
            }
        }

        private void RedrawSearchTableView()
        {
            if (InlineMode)
            {
                _tableView.Hidden = true;
                return;
            }

            if (_tableView != null)
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
                _tableView.EstimatedRowHeight = Theme.CellHeight;

                if (this._direction == Direction.Down)
                {
                    nfloat tableHeight = 0f;

                    var keyboardHeight = _keyboardFrame?.Height;
                    if (KeyboardIsShowing && keyboardHeight != null)
                    {
                        tableHeight = (nfloat)Math.Min(_tableView.ContentSize.Height, UIScreen.MainScreen.Bounds.Height - frame.Y - frame.Height - (nfloat)keyboardHeight);
                    }
                    else
                    {
                        tableHeight = (nfloat)Math.Min(_tableView.ContentSize.Height, UIScreen.MainScreen.Bounds.Height - frame.Y - frame.Height);
                    }

                    if (MaxResultsListHeight > 0)
                    {
                        tableHeight = (nfloat)Math.Min(tableHeight, MaxResultsListHeight);
                    }

                    // Set a bottom margin of 10p
                    if (tableHeight < _tableView.ContentSize.Height)
                    {
                        tableHeight -= TableBottomMargin;
                    }

                    var tableViewFrame = new CGRect(x: 0, y: 0, width: frame.Width - 4, height: tableHeight);
                    var origin = this.ConvertRectToView(tableViewFrame, null);
                    tableViewFrame.X = origin.X + 2 + TableXOffset;
                    tableViewFrame.Y = origin.Y + frame.Height + 2 + TableYOffset;
                    UIView.Animate(0.2, () => {
                        _tableView.Frame = tableViewFrame;
                    });

                    var shadowFrame = new CGRect(x: 0, y: 0, width: frame.Width - 6, height: 1);
                    origin = this.ConvertRectToView(shadowFrame, null);
                    shadowFrame.X = origin.X + 3;
                    shadowFrame.Y = _tableView.Frame.Y;
                    _shadowView.Frame = shadowFrame;
                }
                else
                {
                    var tableHeight = (nfloat)Math.Min((_tableView.ContentSize.Height), (UIScreen.MainScreen.Bounds.Height - frame.Y - Theme.CellHeight));
                    UIView.Animate(0.2, () =>
                    {
                        this._tableView.Frame = new CGRect(x: frame.X + 2, y: (frame.Y - tableHeight), width: frame.Width - 4, height: tableHeight);
                        this._shadowView.Frame = new CGRect(x: frame.X + 3, y: (frame.Y + 3), width: frame.Width - 6, height: 1);
                    });
                }

                Superview?.BringSubviewToFront(_tableView);
                Superview?.BringSubviewToFront(_shadowView);

                if (this.IsFirstResponder)
                {
                    Superview?.BringSubviewToFront(this);
                }

                _tableView.Layer.BorderColor = Theme.BorderColor.CGColor;
                _tableView.Layer.CornerRadius = TableCornerRadius;
                _tableView.SeparatorColor = Theme.SeparatorColor;
                _tableView.BackgroundColor = Theme.BackgroundColor;

                _tableView.ReloadData();
            }
        }

        private void KeyboardWillShow(object sender, UIKeyboardEventArgs e)
        {
            if (!KeyboardIsShowing && IsEditing)
            {
                KeyboardIsShowing = true;
                _keyboardFrame = e.FrameEnd;
                InteractedWith = true;
                PrepareDrawTableResult();
            }
        }

        private void KeyboardWillHide(object sender, UIKeyboardEventArgs e)
        {
            if (KeyboardIsShowing)
            {
                KeyboardIsShowing = false;
                _direction = Direction.Down;
                RedrawSearchTableView();
            }
        }

        private void KeyboardDidChangeFrame(object sender, UIKeyboardEventArgs e)
        {
            var frameEnd = e.FrameEnd;

            if (KeyboardIsShowing)
            {
                Task.Delay(100).ContinueWith(t => InvokeOnMainThread(() =>
                {
                    _keyboardFrame = frameEnd;
                    PrepareDrawTableResult();
                }));
            }
            else 
            {
                _keyboardFrame = frameEnd;
                PrepareDrawTableResult();
            }
        }

        public void TypingDidStop()
        {
            this.UserStoppedTypingHandler?.Invoke();
        }

        // Handle text field changes
        private void TextFieldDidChange(object sender, EventArgs e)
        {
            TextFieldDidChange();
        }

        private void TextFieldDidChange()
        {
            if (!InlineMode && _tableView == null)
            {
                BuildSearchTableView();
            }

            InteractedWith = true;

            // Detect pauses while typing
            _timer?.Invalidate();
            _timer = NSTimer.CreateScheduledTimer(interval: TypingStoppedDelay, repeats: false, block: t => TypingDidStop());

            if (string.IsNullOrWhiteSpace(Text))
            {
                ClearResults();
                _tableView?.ReloadData();
                if (StartVisible || StartVisibleWithoutInteraction)
                {
                    Filter(forceShowAll: true);
                }
                if (_placeholderLabel != null)
                {
                    _placeholderLabel.Text = "";
                }
            }
            else
            {
                Filter(forceShowAll: ForceNoFiltering);
                PrepareDrawTableResult();
            }

            BuildPlaceholderLabel();
        }

        private void TextFieldDidBeginEditing(object sender, EventArgs e)
        {
            if ((StartVisible || StartVisibleWithoutInteraction) && string.IsNullOrWhiteSpace(Text))
            {
                ClearResults();
                Filter(forceShowAll: true);
            }
            if (_placeholderLabel != null)
            {
                _placeholderLabel.AttributedText = null;
            }
        }

        private void TextFieldDidEndEditing(object sender, EventArgs e)
        {
            ClearResults();
            _tableView?.ReloadData();
            if (_placeholderLabel != null)
            {
                _placeholderLabel.AttributedText = null;
            }
        }

        private void TextFieldDidEndEditingOnExit(object sender, EventArgs e)
        {
            var firstElement = _filteredResults.FirstOrDefault();
            if (firstElement != null)
            {
                if (ItemSelectionHandler != null)
                {
                    ItemSelectionHandler(_filteredResults, 0);
                }
                else
                {
                    if (InlineMode && string.IsNullOrEmpty(StartFilteringAfter))
                    {
                        var stringElements = this.Text?.Split(StartFilteringAfter);
                        Text = stringElements.FirstOrDefault() + StartFilteringAfter + firstElement.Title;
                    }
                    else
                    {
                        Text = firstElement.Title;
                    }
                }
            }
        }

        public void HideResultsList()
        {
            var tableFrame = _tableView?.Frame;
            if (tableFrame.HasValue)
            {
                var newFrame = new CGRect(x: tableFrame.Value.X, y: tableFrame.Value.Y, width: tableFrame.Value.Width, height: 0.0f);
                UIView.Animate(0.2, () =>
                {
                    _tableView.Frame = newFrame;
                });
            }
        }

        private void Filter(bool forceShowAll)
        {
            ClearResults();

            if (Text.Length < MinCharactersNumberToStartFiltering)
            {
                return;
            }

            for (int i = 0; i < FilterDataSource.Count; i++)
            {
                var item = FilterDataSource[i];

                if (!InlineMode)
                {
                    // Find text in title and subtitle
                    var titleFilterStart = item.Title.IndexOf(Text, ComparisonOptions);
                    var subtitleFilterStart = !string.IsNullOrEmpty(item?.Subtitle) ? item.Subtitle.IndexOf(Text, ComparisonOptions) : -1;

                    if (titleFilterStart >= 0 || subtitleFilterStart >= 0 || forceShowAll)
                    {
                        item.AttributedTitle = new NSMutableAttributedString(item.Title);
                        item.AttributedSubtitle = new NSMutableAttributedString(!string.IsNullOrEmpty(item.Subtitle) ? item.Subtitle : "");

                        item.AttributedTitle.SetAttributes(HighlightAttributes, new NSRange(titleFilterStart, Text.Length));

                        if (subtitleFilterStart >= 0)
                        {
                            item.AttributedSubtitle.SetAttributes(HighlightAttributesForSubtitle(), new NSRange(subtitleFilterStart, Text.Length));
                        }

                        _filteredResults.Add(item);
                    }
                }
                else
                {
                    var textToFilter = Text.ToLower();

                    if (InlineMode && !string.IsNullOrEmpty(StartFilteringAfter))
                    {
                        var suffixToFilter = textToFilter.Split(StartFilteringAfter).LastOrDefault();
                        if (suffixToFilter != null
                            && (suffixToFilter != "" || StartSuggestingInmediately == true)
                            && (textToFilter != suffixToFilter))
                        {
                            textToFilter = suffixToFilter;
                        }
                        else
                        {
                            _placeholderLabel.Text = "";
                            return;
                        }
                    }

                    if (item.Title.ToLower().IndexOf(textToFilter, ComparisonOptions) == 0)
                    {
                        //var indexFrom = textToFilter.index(textToFilter.startIndex, offsetBy: textToFilter.count)
                        var itemSuffix = item.Title.Substring(textToFilter.Length);

                        item.AttributedTitle = new NSMutableAttributedString(itemSuffix);
                        _filteredResults.Add(item);
                    }
                }
            }

            _tableView?.ReloadData();

            if (InlineMode)
            {
                HandleInlineFiltering();
            }
        }

        // Clean filtered results
        private void ClearResults()
        {
            _filteredResults.Clear();
            _tableView?.RemoveFromSuperview();
        }

        private UIStringAttributes HighlightAttributesForSubtitle()
        {
            var attr = new UIStringAttributes(HighlightAttributes.Dictionary);

            var font = HighlightAttributes?.Font;

            if (font != null)
            {
                attr.Font = UIFont.FromName(font.Name, font.PointSize * _fontConversionRate);
            }

            return attr;
        }

        // Handle inline behaviour
        private void HandleInlineFiltering()
        {
            if (Text != null)
            {
                if (Text == "")
                {
                    if (_placeholderLabel != null)
                    {
                        _placeholderLabel.AttributedText = null;
                    }
                }
                else
                {
                    var firstResult = _filteredResults.FirstOrDefault();
                    if (firstResult != null)
                    {
                        if (_placeholderLabel != null)
                        {
                            _placeholderLabel.AttributedText = firstResult.AttributedTitle;
                        }
                    }
                    else
                    {
                        if (_placeholderLabel != null)
                        {
                            _placeholderLabel.AttributedText = null;
                        }
                    }
                }
            }   
        }

        // MARK: - Prepare for draw table result

        private void PrepareDrawTableResult()
        {
            var frame = Superview?.ConvertRectToCoordinateSpace(Frame, UIApplication.SharedApplication.KeyWindow);
            if (frame == null)
            {
                return;
            }

            if (_keyboardFrame.HasValue)
            {
                var newFrame = frame.Value;
                newFrame.Height += Theme.CellHeight;

                if (_keyboardFrame.Value.IntersectsWith(newFrame))
                {
                    _direction = Direction.Up;
                }
                else
                {
                    _direction = Direction.Down;
                }

                RedrawSearchTableView();
            }
            else
            {
                if (Center.Y + Theme.CellHeight > UIApplication.SharedApplication.KeyWindow.Frame.Height)
                {
                    _direction = Direction.Up;
                }
                else
                {
                    _direction = Direction.Down;
                }
            }
        }

        // IMPLEMENT IUITableViewDelegate, IUITableViewDataSource

        public nint RowsInSection(UITableView tableView, nint section)
        {
            tableView.Hidden = !InteractedWith || (_filteredResults.Count == 0);
            _shadowView.Hidden = !InteractedWith || (_filteredResults.Count == 0);

            if (MaxNumberOfResults > 0)
            {
                return (nint)Math.Min(_filteredResults.Count, MaxNumberOfResults);
            }
            else
            {
                return _filteredResults.Count;
            }
        }

        public UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
        {
            var cell = tableView.DequeueReusableCell(MGSearchTextField.CellIdentifier);

            if (cell == null)
            {
                cell = new UITableViewCell(style: UITableViewCellStyle.Subtitle, reuseIdentifier: MGSearchTextField.CellIdentifier);
            }

            cell.BackgroundColor = UIColor.Clear;
            cell.LayoutMargins = UIEdgeInsets.Zero;
            cell.PreservesSuperviewLayoutMargins = false;
            cell.TextLabel.Font = Theme.Font;
            cell.DetailTextLabel.Font = UIFont.FromName(name: Theme.Font.Name, size: Theme.Font.PointSize * _fontConversionRate);
            cell.TextLabel.TextColor = Theme.FontColor;
            cell.DetailTextLabel.TextColor = Theme.SubtitleFontColor;

            cell.TextLabel.Text = _filteredResults[(indexPath as NSIndexPath).Row].Title;
            cell.DetailTextLabel.Text = _filteredResults[(indexPath as NSIndexPath).Row].Subtitle;
            cell.TextLabel.AttributedText = _filteredResults[(indexPath as NSIndexPath).Row].AttributedTitle;
            cell.DetailTextLabel.AttributedText = _filteredResults[(indexPath as NSIndexPath).Row].AttributedSubtitle;

            cell.ImageView.Image = _filteredResults[(indexPath as NSIndexPath).Row].Image;

            cell.SelectionStyle = UITableViewCellSelectionStyle.None;

            return cell;
        }

        [Export("tableView:heightForRowAtIndexPath:")]
        public nfloat GetHeightForRow(UITableView tableView, NSIndexPath indexPath)
        {
            return Theme.CellHeight;
        }

        [Export("tableView:didSelectRowAtIndexPath:")]
        public void RowSelected(UITableView tableView, NSIndexPath indexPath)
        {
            if (ItemSelectionHandler == null)
            {
                Text = _filteredResults[(indexPath as NSIndexPath).Row].Title;
            }
            else
            {
                var index = indexPath.Row;
                ItemSelectionHandler(_filteredResults, index);
            }

            ClearResults();
        }

    }
}
