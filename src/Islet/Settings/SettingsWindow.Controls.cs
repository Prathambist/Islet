using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Islet
{
    /// <summary>
    /// An on/off switch whose knob slides and whose colour fades, rather than jumping. Its "on"
    /// colour is the window's SwitchOn brush, so it follows the palette.
    /// </summary>
    public sealed class ToggleSwitch : Border
    {
        readonly TranslateTransform knob = new();
        readonly Border onLayer = new() { CornerRadius = new CornerRadius(12) };

        public bool IsOn { get; private set; }

        public event Action<bool>? Toggled;

        public ToggleSwitch(bool on)
        {
            IsOn = on;

            Width = 42;
            Height = 24;
            CornerRadius = new CornerRadius(12);
            SetResourceReference(BackgroundProperty, "SwitchOff");
            Cursor = Cursors.Hand;
            VerticalAlignment = VerticalAlignment.Center;

            onLayer.SetResourceReference(BackgroundProperty, "SwitchOn");
            onLayer.Opacity = on ? 1 : 0;

            knob.X = on ? 18 : 0;

            var layers = new Grid();
            layers.Children.Add(onLayer);
            layers.Children.Add(new Ellipse
            {
                Width = 20,
                Height = 20,
                Fill = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(2, 0, 0, 0),
                RenderTransform = knob,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.3 }
            });
            Child = layers;

            MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                Toggle();
            };
        }

        public void Toggle()
        {
            IsOn = !IsOn;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            knob.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(IsOn ? 18 : 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            onLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(IsOn ? 1 : 0, TimeSpan.FromMilliseconds(200)));

            Toggled?.Invoke(IsOn);
        }
    }

    public partial class SettingsWindow
    {
        sealed class PageInfo
        {
            public required string Name;
            public required string Blurb;
            public required StackPanel Content;
            public required RadioButton Nav;
        }

        // Every row, so search can find it and jump to it
        sealed record Searchable(string Title, string Detail, PageInfo Page, FrameworkElement Row);

        readonly List<PageInfo> pages = new();
        readonly List<Searchable> searchables = new();
        PageInfo page = null!;
        PageInfo? shown;

        // The page last looked at, so reopening Settings goes back to it
        static string? lastPage;

        /// <summary>Quieter text, in the palette's secondary colour.</summary>
        static T Faded<T>(T element) where T : FrameworkElement
        {
            element.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            return element;
        }

        Color HoverTint => ((SolidColorBrush)Resources["Hover"]).Color;

        const double NavStep = 42;

        static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        // ================= Pages and the sidebar =================

        void Page(string name, string glyph, string blurb)
        {
            var nav = new RadioButton
            {
                GroupName = "Pages",
                Style = (Style)FindResource("NavItem"),
                Content = NavContent(glyph, name)
            };

            page = new PageInfo { Name = name, Blurb = blurb, Content = new StackPanel(), Nav = nav };
            var thisPage = page;

            nav.Checked += (s, e) => ShowPage(thisPage);
            Nav.Children.Add(nav);
            pages.Add(page);
        }

        StackPanel NavContent(string glyph, string name)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock
            {
                Text = glyph,
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 15,
                Width = 30,
                VerticalAlignment = VerticalAlignment.Center
            });
            content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            return content;
        }

        /// <summary>Shows the page with this name, as if picked in the sidebar.</summary>
        public void Open(string name)
        {
            var wanted = pages.FirstOrDefault(p => p.Name == name);

            if (wanted != null)
                wanted.Nav.IsChecked = true;
        }

        void OpenFirstPage()
        {
            var start = pages.FirstOrDefault(p => p.Name == lastPage) ?? pages[0];
            start.Nav.IsChecked = true;
        }

        void ShowPage(PageInfo next, bool animate = true)
        {
            if (SearchBox.Text.Length > 0)
            {
                // Picking a page ends a search
                SearchBox.Text = "";
            }

            shown = next;
            lastPage = next.Name;
            PageTitle.Text = next.Name;
            PageBlurb.Text = next.Blurb;
            PageContent.Content = next.Content;
            Scroller.ScrollToTop();

            // The highlight glides to the page's row in the sidebar
            int index = pages.IndexOf(next);
            NavHighlight.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
            NavHighlightMove.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(index * NavStep, TimeSpan.FromMilliseconds(animate ? 260 : 0))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });

            if (animate)
                SlideIn();
        }

        // A page arrives with a short fade and rise
        void SlideIn()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            PageMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
        }

        // ================= Search =================

        void Search_Changed(object sender, TextChangedEventArgs e)
        {
            string query = SearchBox.Text.Trim();
            SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (query.Length == 0)
            {
                if (shown != null)
                {
                    PageTitle.Text = shown.Name;
                    PageBlurb.Text = shown.Blurb;
                    PageContent.Content = shown.Content;
                    NavHighlight.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
                }

                return;
            }

            var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var matches = searchables
                .Where(s => words.All(w => (s.Title + " " + s.Detail + " " + s.Page.Name).Contains(w, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            PageTitle.Text = "Search";
            PageBlurb.Text = matches.Count == 0
                ? $"Nothing matches \"{query}\"."
                : $"{matches.Count} {(matches.Count == 1 ? "setting matches" : "settings match")} \"{query}\".";

            var results = new StackPanel();

            foreach (var group in matches.GroupBy(m => m.Page))
            {
                var card = Group(group.Key.Name, results);

                foreach (var match in group)
                    AddToCard(card, Result(match));
            }

            PageContent.Content = results;
            NavHighlight.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, TimeSpan.FromMilliseconds(150)));
            Scroller.ScrollToTop();
        }

        FrameworkElement Result(Searchable match)
        {
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = match.Title, FontSize = 13.5 });

            if (match.Detail.Length > 0)
                text.Children.Add(Faded(new TextBlock
                {
                    Text = match.Detail,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }));

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(text);

            var go = new TextBlock
            {
                Text = "",
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };

            Faded(go);
            Grid.SetColumn(go, 1);
            row.Children.Add(go);

            var holder = Hoverable(row);
            holder.Cursor = Cursors.Hand;
            holder.MouseLeftButtonUp += (s, e) => JumpTo(match);
            return holder;
        }

        // Opens the setting's page, scrolls to it and makes it glow for a moment
        void JumpTo(Searchable match)
        {
            SearchBox.Text = "";
            match.Page.Nav.IsChecked = true;
            ShowPage(match.Page);

            Dispatcher.BeginInvoke(() =>
            {
                match.Row.BringIntoView();
                Flash(match.Row);
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        void Flash(FrameworkElement row)
        {
            if (row is not Border border || border.Background is not SolidColorBrush brush || brush.IsFrozen)
                return;

            // A glow in the palette's accent
            var accent = palette.Accent;
            var flash = Color.FromArgb(70, accent.R, accent.G, accent.B);

            var glow = new ColorAnimationUsingKeyFrames();
            glow.KeyFrames.Add(new EasingColorKeyFrame(flash, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
            glow.KeyFrames.Add(new EasingColorKeyFrame(flash, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700))));
            glow.KeyFrames.Add(new EasingColorKeyFrame(Colors.Transparent, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1600))));
            brush.BeginAnimation(SolidColorBrush.ColorProperty, glow);
        }

        // ================= Cards and rows =================

        /// <summary>A titled card on the page being built, or on another panel; rows go inside it.</summary>
        StackPanel Group(string? title, StackPanel? on = null)
        {
            on ??= page.Content;

            if (title != null)
            {
                on.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(4, on.Children.Count == 0 ? 0 : 22, 0, 8)
                });
            }

            var rows = new StackPanel();
            var card = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Child = rows,
                ClipToBounds = true
            };

            card.SetResourceReference(Border.BackgroundProperty, "CardBg");
            card.SetResourceReference(Border.BorderBrushProperty, "Divider");
            on.Children.Add(card);
            return rows;
        }

        static void AddToCard(StackPanel card, UIElement row)
        {
            if (card.Children.Count > 0)
            {
                var line = new Border { Height = 1, Margin = new Thickness(16, 0, 16, 0) };
                line.SetResourceReference(Border.BackgroundProperty, "Divider");
                card.Children.Add(line);
            }

            card.Children.Add(row);
        }

        // A row that softly lights up under the pointer
        Border Hoverable(UIElement content)
        {
            var background = new SolidColorBrush(Colors.Transparent);
            var row = new Border { Padding = new Thickness(16, 12, 16, 12), Background = background, Child = content };

            row.MouseEnter += (s, e) => background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(HoverTint, TimeSpan.FromMilliseconds(120)));
            row.MouseLeave += (s, e) => background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Colors.Transparent, TimeSpan.FromMilliseconds(220)));
            return row;
        }

        /// <summary>
        /// One setting: its name and a short explanation on the left, its control on the right,
        /// or below when the control is wide. Registered for search.
        /// </summary>
        Border Row(StackPanel card, string title, string? detail, FrameworkElement? side, FrameworkElement? below, out TextBlock detailText)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = title, FontSize = 13.5, TextWrapping = TextWrapping.Wrap });

            detailText = Faded(new TextBlock
            {
                Text = detail ?? "",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
                Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible
            });
            words.Children.Add(detailText);
            grid.Children.Add(words);

            if (side != null)
            {
                side.Margin = new Thickness(20, 0, 0, 0);
                side.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(side, 1);
                grid.Children.Add(side);
            }

            if (below != null)
            {
                below.Margin = new Thickness(0, 10, 0, 0);
                Grid.SetRow(below, 1);
                Grid.SetColumnSpan(below, 2);
                grid.Children.Add(below);
            }

            var row = Hoverable(grid);
            AddToCard(card, row);
            searchables.Add(new Searchable(title, detail ?? "", page, row));
            return row;
        }

        Border Row(StackPanel card, string title, string? detail = null, FrameworkElement? side = null, FrameworkElement? below = null) =>
            Row(card, title, detail, side, below, out _);

        /// <summary>An on/off setting; clicking anywhere on the row flips it.</summary>
        void SwitchRow(StackPanel card, string title, string? detail, bool value, Action<bool> changed)
        {
            var toggle = new ToggleSwitch(value);
            toggle.Toggled += changed;

            var row = Row(card, title, detail, toggle);
            row.Cursor = Cursors.Hand;
            row.MouseLeftButtonUp += (s, e) => toggle.Toggle();
        }

        /// <summary>A choice between a few options; long lists sit under the explanation.</summary>
        Border ChoiceRow(StackPanel card, string title, string? detail, string[] options, int selected, Action<int> changed, out TextBlock detailText)
        {
            var choices = Segmented(options, selected, changed);
            bool wide = options.Length >= 4 || options.Sum(o => o.Length) > 24;

            return wide
                ? Row(card, title, detail, null, choices, out detailText)
                : Row(card, title, detail, choices, null, out detailText);
        }

        void ChoiceRow(StackPanel card, string title, string? detail, string[] options, int selected, Action<int> changed) =>
            ChoiceRow(card, title, detail, options, selected, changed, out _);

        Border Segmented(string[] options, int selected, Action<int> changed)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            string group = Guid.NewGuid().ToString();

            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                var option = new RadioButton
                {
                    Content = options[i],
                    GroupName = group,
                    IsChecked = i == selected,
                    Style = (Style)FindResource("Segment")
                };

                option.Checked += (s, e) => changed(index);
                panel.Children.Add(option);
            }

            var track = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = panel
            };

            track.SetResourceReference(Border.BackgroundProperty, "Control");
            return track;
        }

        Button PillButton(string text, Action click)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = text },
                Style = (Style)FindResource("Pill")
            };

            button.Click += (s, e) => click();
            return button;
        }

        Button PillButton(string text, Func<Task> click) =>
            PillButton(text, () => { _ = click(); });

        static StackPanel Buttons(params UIElement[] buttons)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };

            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] is FrameworkElement element && i > 0)
                    element.Margin = new Thickness(8, 0, 0, 0);

                row.Children.Add(buttons[i]);
            }

            return row;
        }

        static TextBlock Note(string text) => Faded(new TextBlock
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });

        static ControlTemplate TransparentTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            border.FirstChild.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            border.FirstChild.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            template.VisualTree = border;
            return template;
        }
    }
}
