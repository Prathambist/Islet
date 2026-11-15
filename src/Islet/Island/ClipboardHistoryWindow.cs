using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Islet
{
    /// <summary>
    /// Browse, search and copy again what you've copied. Click an entry (or press Enter on the
    /// first match) to put it back on the clipboard; pin keeps it past the retention period.
    /// </summary>
    sealed class ClipboardHistoryWindow : Window
    {
        static readonly Brush Back = Frozen(Color.FromRgb(0x1C, 0x1C, 0x1E));
        static readonly Brush Card = Frozen(Color.FromRgb(0x2C, 0x2C, 0x2E));
        static readonly Brush CardHover = Frozen(Color.FromRgb(0x3A, 0x3A, 0x3C));
        static readonly Brush TextBrush = Frozen(Color.FromRgb(0xF2, 0xF2, 0xF7));
        static readonly Brush Faded = Frozen(Color.FromRgb(0x98, 0x98, 0x9F));
        static readonly Brush Accent = Frozen(Color.FromRgb(0x0A, 0x84, 0xFF));
        static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

        readonly ClipboardHistory history;
        readonly TextBox search = new();
        readonly StackPanel list = new();
        readonly TextBlock status = new();

        public ClipboardHistoryWindow(ClipboardHistory history)
        {
            this.history = history;

            Title = "Islet clipboard history";
            Width = 440;
            Height = 560;
            MinWidth = 320;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Back;
            Foreground = TextBrush;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            FontSize = 13;

            var root = new DockPanel { Margin = new Thickness(14) };

            // Search
            search.Padding = new Thickness(8, 6, 8, 6);
            search.Background = Card;
            search.Foreground = TextBrush;
            search.CaretBrush = TextBrush;
            search.BorderThickness = new Thickness(0);
            search.ToolTip = "Search your clipboard history";
            search.TextChanged += (s, e) => Build();
            search.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && history.Search(search.Text).FirstOrDefault() is ClipboardEntry first)
                {
                    CopyAndClose(first);
                    e.Handled = true;
                }
            };

            var placeholder = new TextBlock
            {
                Text = "Search",
                Foreground = Faded,
                Margin = new Thickness(11, 6, 0, 0),
                IsHitTestVisible = false
            };
            search.TextChanged += (s, e) => placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            var searchBox = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            searchBox.Children.Add(search);
            searchBox.Children.Add(placeholder);
            DockPanel.SetDock(searchBox, Dock.Top);
            root.Children.Add(searchBox);

            // Footer: how many, and Clear
            var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var clear = TextButton("Clear unpinned", () =>
            {
                if (MessageBox.Show(this, "Delete every clipboard entry that isn't pinned?", "Islet",
                        MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
                    history.Clear();
            });
            DockPanel.SetDock(clear, Dock.Right);
            footer.Children.Add(clear);
            status.Foreground = Faded;
            status.FontSize = 12;
            status.VerticalAlignment = VerticalAlignment.Center;
            footer.Children.Add(status);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            root.Children.Add(new ScrollViewer
            {
                Content = list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            });

            Content = root;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                    Close();
            };

            history.Changed += OnChanged;
            Closed += (s, e) => history.Changed -= OnChanged;

            Build();
        }

        public void FocusSearch()
        {
            search.Focus();
            search.SelectAll();
        }

        void OnChanged() => Dispatcher.BeginInvoke(new Action(Build));

        void Build()
        {
            list.Children.Clear();
            var matches = history.Search(search.Text);

            status.Text = history.Items.Count == 0
                ? "Nothing yet. Text you copy shows up here."
                : search.Text.Length > 0
                    ? $"{matches.Count} of {history.Items.Count}"
                    : $"{history.Items.Count} item{(history.Items.Count == 1 ? "" : "s")}";

            foreach (var entry in matches.Take(200))
                list.Children.Add(EntryRow(entry));
        }

        FrameworkElement EntryRow(ClipboardEntry entry)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock
            {
                Text = Preview(entry.Text),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 54
            });
            words.Children.Add(new TextBlock
            {
                Text = (entry.Pinned ? "Pinned · " : "") + Ago(DateTime.UtcNow - entry.CopiedAt),
                Foreground = entry.Pinned ? Accent : Faded,
                FontSize = 11.5,
                Margin = new Thickness(0, 3, 0, 0)
            });
            grid.Children.Add(words);

            var pin = IconButton(entry.Pinned ? "" : "", entry.Pinned ? "Unpin" : "Pin, so it's never deleted automatically",
                () => history.SetPinned(entry.Id, !entry.Pinned));
            if (entry.Pinned)
                pin.Foreground = Accent;
            Grid.SetColumn(pin, 1);
            grid.Children.Add(pin);

            var remove = IconButton("", "Delete", () => history.Remove(entry.Id));
            Grid.SetColumn(remove, 2);
            grid.Children.Add(remove);

            var row = new Border
            {
                Background = Card,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 9, 6, 9),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                ToolTip = "Click to copy",
                Child = grid
            };

            row.MouseEnter += (s, e) => row.Background = CardHover;
            row.MouseLeave += (s, e) => row.Background = Card;
            row.MouseLeftButtonUp += (s, e) => CopyAndClose(entry);
            return row;
        }

        void CopyAndClose(ClipboardEntry entry)
        {
            // The clipboard can be busy for a moment with another app
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Clipboard.SetText(entry.Text);
                    Close();
                    return;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    Thread.Sleep(50);
                }
            }

            status.Text = "The clipboard is busy. Try again.";
        }

        public static string Preview(string text)
        {
            text = text.Trim().Replace("\r\n", " ⏎ ").Replace('\n', ' ').Replace('\t', ' ');
            return text.Length > 300 ? text[..300] + "…" : text;
        }

        public static string Ago(TimeSpan span) => span switch
        {
            { TotalMinutes: < 1 } => "Just now",
            { TotalHours: < 1 } => $"{(int)span.TotalMinutes} min ago",
            { TotalDays: < 1 } => $"{(int)span.TotalHours} h ago",
            { TotalDays: < 2 } => "Yesterday",
            _ => $"{(int)span.TotalDays} days ago"
        };

        static Button IconButton(string glyph, string tip, Action click)
        {
            var button = TextButton(glyph, click);
            button.FontFamily = Icons;
            button.FontSize = 12;
            button.Width = 30;
            button.Height = 30;
            button.Foreground = Faded;
            button.ToolTip = tip;
            return button;
        }

        static Button TextButton(string text, Action click)
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.PaddingProperty, new Thickness(6, 2, 6, 2));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            template.VisualTree = border;

            var button = new Button
            {
                Content = text,
                Foreground = Accent,
                Cursor = Cursors.Hand,
                Template = template
            };

            // A button marks the mouse-up handled, so the row underneath doesn't copy too
            button.Click += (s, e) =>
            {
                e.Handled = true;
                click();
            };
            return button;
        }

        static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
