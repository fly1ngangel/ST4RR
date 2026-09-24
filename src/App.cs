using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Starrfind
{
    public static class L
    {
        public static bool English;
        public static string T(string ru, string en)
        {
            return English ? en : ru;
        }

        public static string Source(string s)
        {
            if (!English)
                return s ?? "";
            return (s ?? "").Replace("Участник боя", "Battle participant").Replace("индекс", "index").Replace("рейтинг", "ranking").Replace("Вручную", "Manual").Replace("Импорт", "Import");
        }
    }

    public static class Program
    {
        public static string DataDir;
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (args.Contains("--test"))
                    return Tests.Run(args);
                bool smoke = args.Contains("--smoke");
                DataDir = smoke ? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smoke-data") : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ST4RRF1ND");
                Directory.CreateDirectory(DataDir);
                bool created;
                using (var mutex = new Mutex(true, smoke ? "ST4RR-smoke" : "Local\\ST4RRF1ND-" + Environment.UserName, out created))
                {
                    if (!created)
                    {
                        MessageBox.Show("ST4RR is already running / Приложение уже запущено.");
                        return 0;
                    }

                    var app = new Application();
                    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Theme.xaml"))
                        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream);
                    app.DispatcherUnhandledException += (s, e) =>
                    {
                        File.AppendAllText(System.IO.Path.Combine(DataDir, "errors.log"), DateTime.UtcNow + " " + e.Exception + Environment.NewLine);
                        MessageBox.Show(L.T("Не удалось завершить действие. Подробности в errors.log.", "Could not complete this action. Details are in errors.log."), "ST4RR");
                        e.Handled = true;
                    };
                    var options = Options.Load(DataDir);
                    if (smoke)
                        options.Language = args.Contains("--en") ? "en" : "ru";
                    options.Animations = true;
                    options.Blur = true;
                    var win = new MainWindow(options, smoke);
                    app.MainWindow = win;
                    app.Run(win);
                    win.DisposeData();
                    return 0;
                }
            }
            catch (Exception e)
            {
                try
                {
                    File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-error.log"), e.ToString());
                }
                catch
                {
                }

                MessageBox.Show(e.Message, "ST4RR");
                return 1;
            }
        }
    }

    public class MainWindow : Window
    {
        Store db;
        readonly Options options;
        readonly Sources sources;
        readonly bool smoke;
        readonly Dictionary<string, Button> navigation = new Dictionary<string, Button>();
        readonly List<string> compare = new List<string>();
        StackPanel page;
        Grid shell;
        TextBlock status;
        ProgressBar progress;
        Button cancelButton;
        TextBlock header;
        CancellationTokenSource cancellation;
        bool busy;
        bool closing;
        string current = "search";
        Player selected;
        TextBox query, minBox, maxBox, clubFilter, brawlerFilter;
        ComboBox yearBox, sortBox;
        DatePicker fromDate, toDate;
        CheckBox includeUnknown, fuzzy;
        StackPanel resultList;
        TextBlock resultCount;
        List<Player> visiblePlayers = new List<Player>();
        int localPage, remotePage = 1;
        string lastQuery = "";
        bool favoriteOnly;
        DispatcherTimer watcher;
        Grid startupHost, loadingLayer;
        bool initialized, loadingShown, loadingDismissed, spinnerAnimated;
        IntPtr startupHandle;
        static string T(string ru, string en) => L.T(ru, en);
        static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
        public MainWindow(Options opts, bool isSmoke)
        {
            options = opts;
            smoke = isSmoke;
            sources = new Sources(options);
            SetLanguage();
            Title = "ST4RR";
            Width = Math.Min(1320, SystemParameters.WorkArea.Width - 40);
            Height = Math.Min(860, SystemParameters.WorkArea.Height - 40);
            MinWidth = 1000;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brush("#101116");
            Foreground = Brush("#F2F3F7");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 38, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                if (s != null)
                {
                    var decoder = new IconBitmapDecoder(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    Icon = decoder.Frames[0];
                }

            ShowLoadingWindow();
            watcher = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(Math.Max(5, options.IntervalMinutes))
            };
            watcher.Tick += async (s, e) =>
            {
                if (initialized && options.AutoRefresh && !busy && !smoke)
                    await Track();
            };
            watcher.Start();
            StateChanged += (s, e) =>
            {
                if (WindowState != WindowState.Minimized)
                    RevealWindow();
            };
            Closing += (s, e) =>
            {
                closing = true;
                watcher.Stop();
                cancellation?.Cancel();
            };
            PreviewKeyDown += (s, e) =>
            {
                if (!initialized)
                    return;
                if (e.Key == Key.Escape)
                {
                    cancellation?.Cancel();
                }

                if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    Navigate("search");
                    query.Focus();
                    query.SelectAll();
                    e.Handled = true;
                }
            };
            Loaded += async (s, e) =>
            {
                await InitializeWindow();
                if (closing || !initialized)
                    return;
                if (smoke)
                    await Smoke();
                else if (db.Players.Count() == 0 && options.FindEnabled)
                    await Run(async ct =>
                    {
                        var players = await sources.Ranking("global", ct);
                        foreach (var p in players)
                            db.Save(p);
                        if (current == "search")
                            RefreshResults();
                        SetStatus(T("Стартовый индекс загружен: ", "Starter index loaded: ") + players.Count + " · BrawlFind");
                    }, T("Готовим стартовую подборку игроков…", "Loading a starter player selection…"));
            };
        }

        public void DisposeData()
        {
            db?.Dispose();
            sources.Dispose();
        }

        void ShowLoadingWindow()
        {
            startupHost = new Grid
            {
                Background = Brush("#101116")
            };
            loadingLayer = new Grid
            {
                Background = Brush("#101116")
            };
            startupHost.Children.Add(loadingLayer);
            Content = startupHost;
            loadingLayer.Children.Add(TitleBar());
            var spinner = BootVisuals.Spinner();
            spinner.HorizontalAlignment = HorizontalAlignment.Center;
            spinner.VerticalAlignment = VerticalAlignment.Center;
            loadingLayer.Children.Add(spinner);
            spinner.Loaded += (s, e) =>
            {
                loadingShown = true;
                spinnerAnimated = Descendants<System.Windows.Shapes.Path>(spinner).Any(x => x.RenderTransform.HasAnimatedProperties);
                startupHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            };
        }

        async Task InitializeWindow()
        {
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                }, DispatcherPriority.ApplicationIdle);
                if (smoke)
                {
                    var dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
                    Directory.CreateDirectory(dir);
                    Shot(System.IO.Path.Combine(dir, "startup-" + (L.English ? "en" : "ru") + ".png"));
                }

                db = await Task.Run(() => new Store(System.IO.Path.Combine(Program.DataDir, "library.db")));
                if (closing)
                {
                    db.Dispose();
                    db = null;
                    return;
                }

                BuildShell();
                Navigate("search");
                shell.Opacity = 0;
                UpdateLayout();
                await Dispatcher.InvokeAsync(() =>
                {
                }, DispatcherPriority.Render);
                var duration = TimeSpan.FromMilliseconds(550);
                loadingLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration));
                shell.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                var move = new TranslateTransform(0, 12);
                shell.RenderTransform = move;
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                await Task.Delay(560);
                if (closing)
                    return;
                startupHost.Children.Remove(loadingLayer);
                loadingLayer = null;
                startupHost.Children.Remove(shell);
                Content = shell;
                startupHost = null;
                loadingDismissed = true;
                initialized = true;
            }
            catch (Exception ex)
            {
                if (!closing)
                {
                    File.AppendAllText(System.IO.Path.Combine(Program.DataDir, "errors.log"), ex + Environment.NewLine);
                    var panel = Vertical(Text(T("Не удалось открыть базу", "Could not open the library"), 22, null, true), Muted(ErrorMessage(ex)), Btn(T("Закрыть", "Close"), Close));
                    panel.HorizontalAlignment = HorizontalAlignment.Center;
                    panel.VerticalAlignment = VerticalAlignment.Center;
                    loadingLayer.Children.Clear();
                    loadingLayer.Children.Add(panel);
                }
            }
        }

        Grid TitleBar()
        {
            var bar = new Grid
            {
                Height = 38,
                VerticalAlignment = VerticalAlignment.Top,
                Background = Brush("#15151C")
            };
            bar.ColumnDefinitions.Add(new ColumnDefinition());
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.Children.Add(new TextBlock { Text = "ST4RR", FontSize = 11, Foreground = Brush("#A4A2B2"), Margin = new Thickness(20, 11, 0, 0) });
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            foreach (var id in new[]
            {
                "minimize",
                "maximize",
                "close"
            }

            )
            {
                var action = id;
                var b = Btn("", () =>
                {
                    if (action == "close")
                        Close();
                    else if (action == "maximize")
                        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                    else if (initialized)
                        MinimizeAnimated();
                    else
                        WindowState = WindowState.Minimized;
                });
                b.Content = Branding.Icon(id, 14);
                b.ToolTip = id == "minimize" ? T("Свернуть", "Minimize") : id == "maximize" ? T("Развернуть / восстановить", "Maximize / restore") : T("Закрыть", "Close");
                System.Windows.Automation.AutomationProperties.SetName(b, (string)b.ToolTip);
                b.Width = 48;
                b.Height = 38;
                b.MinHeight = 0;
                b.Padding = new Thickness(0);
                b.Background = Brush("#15151C");
                b.BorderThickness = new Thickness(0);
                WindowChrome.SetIsHitTestVisibleInChrome(b, true);
                buttons.Children.Add(b);
            }

            Grid.SetColumn(buttons, 1);
            bar.Children.Add(buttons);
            return bar;
        }

        void SetLanguage()
        {
            L.English = options.Language == "en" || (options.Language == "auto" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "ru");
            Language = System.Windows.Markup.XmlLanguage.GetLanguage(L.English ? "en-US" : "ru-RU");
        }

        TextBlock Text(string s, double size = 13, string color = null, bool bold = false)
        {
            return new TextBlock
            {
                Text = s,
                FontSize = size,
                Foreground = Brush(color ?? "#F2F3F7"),
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap
            };
        }

        TextBlock Muted(string s, double size = 12) => Text(s, size, "#A2A6B8");
        StackPanel Vertical(params UIElement[] items)
        {
            var p = new SpacedStack
            {
                Gap = 8
            };
            foreach (var item in items)
                p.Children.Add(item);
            return p;
        }

        GapRow Row(params UIElement[] items)
        {
            var row = new GapRow();
            foreach (var item in items)
            {
                if (item is FrameworkElement f)
                {
                    f.Margin = new Thickness(0);
                    f.VerticalAlignment = VerticalAlignment.Bottom;
                }

                row.Children.Add(item);
            }

            return row;
        }

        FormGrid Fields(params UIElement[] items)
        {
            var grid = new FormGrid
            {
                MaxColumns = Math.Min(4, items.Length)
            };
            foreach (FrameworkElement field in items)
            {
                field.Width = double.NaN;
                field.HorizontalAlignment = HorizontalAlignment.Stretch;
                foreach (var input in DescendantsLogical<Control>(field))
                {
                    input.Width = double.NaN;
                    input.HorizontalAlignment = HorizontalAlignment.Stretch;
                }

                grid.Children.Add(field);
            }

            return grid;
        }

        static IEnumerable<T> DescendantsLogical<T>(DependencyObject parent)
            where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(parent))
            {
                if (child is T match)
                    yield return match;
                if (child is DependencyObject d)
                    foreach (var nested in DescendantsLogical<T>(d))
                        yield return nested;
            }
        }

        Border Card(UIElement child, int padding = 24)
        {
            return new Border
            {
                Background = Brush("#E51A1C24"),
                BorderBrush = Brush("#30333F"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(padding),
                Child = child
            };
        }

        Button Btn(string label, Action action, bool primary = false)
        {
            var b = new Button
            {
                Content = label,
                MinHeight = 44,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (primary)
                b.Style = (Style)Application.Current.Resources["Primary"];
            b.Click += (s, e) => action();
            b.MouseEnter += (s, e) =>
            {
                if (options.Animations)
                    b.BeginAnimation(OpacityProperty, new DoubleAnimation(.92, TimeSpan.FromMilliseconds(100)));
            };
            b.MouseLeave += (s, e) =>
            {
                if (options.Animations)
                    b.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
            };
            return b;
        }

        Button AsyncBtn(string label, Func<Task> action, bool primary = false)
        {
            return Btn(label, async () => await action(), primary);
        }

        TextBox Input(string text = "", double width = 180)
        {
            return new TextBox
            {
                Text = text,
                Width = width,
                MinHeight = 44,
                MaxLength = 200
            };
        }

        CheckBox Check(string label, bool value = false)
        {
            return new CheckBox
            {
                Content = label,
                IsChecked = value
            };
        }

        ComboBox Choice(IEnumerable<KeyValuePair<string, string>> items, string selectedValue = null, double width = 180)
        {
            var c = new ComboBox
            {
                Width = width
            };
            foreach (var item in items)
                c.Items.Add(new ComboBoxItem { Content = item.Value, Tag = item.Key });
            c.SelectedIndex = 0;
            if (selectedValue != null)
                foreach (ComboBoxItem x in c.Items)
                    if ((string)x.Tag == selectedValue)
                        c.SelectedItem = x;
            return c;
        }

        ComboBox Choice(string[] items, double width = 180) => Choice(items.Select(s => new KeyValuePair<string, string>(s, s)), null, width);
        string Value(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        StackPanel Field(string label, UIElement input)
        {
            var t = Muted(label);
            t.VerticalAlignment = VerticalAlignment.Bottom;
            var labelBox = new Grid
            {
                MinHeight = 28
            };
            labelBox.Children.Add(t);
            var field = new SpacedStack
            {
                Gap = 8
            };
            field.Children.Add(labelBox);
            field.Children.Add(input);
            field.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (input is FrameworkElement element && !double.IsNaN(element.Width))
            {
                field.Width = element.Width;
                field.HorizontalAlignment = HorizontalAlignment.Left;
                element.HorizontalAlignment = HorizontalAlignment.Stretch;
            }

            return field;
        }

        void Link(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https")
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        void Copy(string text)
        {
            try
            {
                Clipboard.SetText(text ?? "");
                SetStatus(T("Скопировано", "Copied"));
            }
            catch
            {
                SetStatus(T("Буфер обмена занят", "Clipboard is busy"));
            }
        }

        void SetStatus(string text)
        {
            if (status != null)
                status.Text = text;
        }

        void BuildShell()
        {
            shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
            shell.RowDefinitions.Add(new RowDefinition());
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
            if (startupHost != null)
                startupHost.Children.Insert(0, shell);
            else
                Content = shell;
            var ambient = new Canvas
            {
                IsHitTestVisible = false,
                ClipToBounds = true
            };
            Grid.SetRowSpan(ambient, 3);
            shell.Children.Add(ambient);
            var glow = new Ellipse
            {
                Width = 480,
                Height = 330,
                Fill = Brush("#28233F"),
                Opacity = .46
            };
            Canvas.SetLeft(glow, 650);
            Canvas.SetTop(glow, -160);
            if (options.Blur)
                glow.Effect = new BlurEffect
                {
                    Radius = 85
                };
            else
                glow.Opacity = .15;
            ambient.Children.Add(glow);
            shell.Children.Add(TitleBar());
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(body, 1);
            shell.Children.Add(body);
            var side = new Grid
            {
                Margin = new Thickness(0),
                Background = Brush("#DF171820")
            };
            side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            side.RowDefinitions.Add(new RowDefinition());
            side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.Children.Add(side);
            var brand = Branding.Lockup();
            brand.Margin = new Thickness(24, 24, 12, 24);
            side.Children.Add(brand);
            var menu = new SpacedStack
            {
                Gap = 4,
                Margin = new Thickness(12, 0, 12, 0)
            };
            var menuScroll = new ScrollViewer
            {
                Content = menu,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(menuScroll, 1);
            side.Children.Add(menuScroll);
            navigation.Clear();
            AddNav(menu, "search", "⌕", T("Поиск игроков", "Find players"));
            AddNav(menu, "library", "▤", T("Моя база", "My library"));
            AddNav(menu, "favorites", "☆", T("Избранное", "Saved players"));
            menu.Children.Add(new Border { Height = 1, Background = Brush("#30303D"), Margin = new Thickness(12, 16, 12, 16) });
            AddNav(menu, "battles", "◷", T("Архив боёв", "Battle archive"));
            AddNav(menu, "compare", "⇄", T("Сравнение", "Compare"));
            AddNav(menu, "clubs", "◇", T("Клубы", "Clubs"));
            AddNav(menu, "rankings", "↗", T("Обзор игроков", "Discover players"));
            AddNav(menu, "events", "◈", T("События", "Events"));
            var bottom = Vertical();
            bottom.Margin = new Thickness(12, 12, 12, 16);
            AddNav(bottom, "settings", "⚙", T("Настройки", "Settings"));
            AddNav(bottom, "about", "ⓘ", T("О приложении", "About"));
            Grid.SetRow(bottom, 2);
            side.Children.Add(bottom);
            var main = new Grid
            {
                Margin = new Thickness(28, 24, 28, 0)
            };
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition());
            Grid.SetColumn(main, 1);
            body.Children.Add(main);
            header = Text("", 25, null, true);
            header.Margin = new Thickness(0, 0, 0, 20);
            main.Children.Add(header);
            page = new SpacedStack
            {
                Gap = 20
            };
            var scroll = new ScrollViewer
            {
                Content = page,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 12, 24)
            };
            Grid.SetRow(scroll, 1);
            main.Children.Add(scroll);
            var footer = new Grid
            {
                Background = Brush("#15161D"),
                Margin = new Thickness(0)
            };
            footer.ColumnDefinitions.Add(new ColumnDefinition());
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(footer, 2);
            shell.Children.Add(footer);
            status = Muted(T("Готово. Введите ник или тег игрока.", "Ready. Enter a player name or tag."), 11);
            status.TextTrimming = TextTrimming.CharacterEllipsis;
            status.TextWrapping = TextWrapping.NoWrap;
            status.VerticalAlignment = VerticalAlignment.Center;
            status.Margin = new Thickness(18, 0, 12, 0);
            status.ToolTip = status.Text;
            footer.Children.Add(status);
            progress = new ProgressBar
            {
                Height = 3,
                IsIndeterminate = true,
                Foreground = Brush("#B9ADFF"),
                Background = Brush("#26212F"),
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 16, 0)
            };
            Grid.SetColumn(progress, 1);
            footer.Children.Add(progress);
            cancelButton = Btn(T("Отмена", "Cancel"), () => cancellation?.Cancel());
            cancelButton.MinHeight = 0;
            cancelButton.Padding = new Thickness(12, 3, 12, 3);
            cancelButton.Margin = new Thickness(0, 4, 12, 4);
            cancelButton.Visibility = Visibility.Collapsed;
            Grid.SetColumn(cancelButton, 2);
            footer.Children.Add(cancelButton);
        }

        void AddNav(StackPanel parent, string id, string icon, string label)
        {
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            content.ColumnDefinitions.Add(new ColumnDefinition());
            var glyph = Branding.Icon(id);
            glyph.HorizontalAlignment = HorizontalAlignment.Left;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(glyph);
            var caption = Text(label, 13);
            caption.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(caption, 1);
            content.Children.Add(caption);
            var b = Btn("", () => Navigate(id));
            b.Content = content;
            b.Tag = label;
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            b.Height = 44;
            b.Padding = new Thickness(12, 0, 10, 0);
            b.Margin = new Thickness(0);
            b.Background = Brush("#00171820");
            b.BorderThickness = new Thickness(0);
            System.Windows.Automation.AutomationProperties.SetName(b, label);
            parent.Children.Add(b);
            navigation[id] = b;
        }

        bool minimizing;
        async void MinimizeAnimated()
        {
            if (minimizing)
                return;
            minimizing = true;
            try
            {
                shell.IsHitTestVisible = false;
                var scale = new ScaleTransform(1, 1);
                shell.RenderTransformOrigin = new Point(.5, 1);
                shell.RenderTransform = scale;
                var duration = TimeSpan.FromMilliseconds(130);
                shell.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration));
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, .97, duration));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, .97, duration));
                await Task.Delay(135);
                if (!closing)
                    WindowState = WindowState.Minimized;
            }
            finally
            {
                shell.IsHitTestVisible = true;
                minimizing = false;
            }
        }

        void RevealWindow()
        {
            if (shell == null)
                return;
            shell.IsHitTestVisible = true;
            var duration = TimeSpan.FromMilliseconds(210);
            shell.BeginAnimation(OpacityProperty, new DoubleAnimation(.3, 1, duration));
            var scale = new ScaleTransform(.985, .985);
            shell.RenderTransformOrigin = new Point(.5, .5);
            shell.RenderTransform = scale;
            var ease = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.985, 1, duration) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.985, 1, duration) { EasingFunction = ease });
        }

        void Navigate(string id)
        {
            foreach (var picker in Descendants<DatePicker>(page))
                picker.IsDropDownOpen = false;
            current = id;
            selected = null;
            page.Children.Clear();
            foreach (var kv in navigation)
            {
                kv.Value.Background = Brush(kv.Key == id ? "#343044" : "#00171820");
                kv.Value.Foreground = Brush(kv.Key == id ? "#DDD5FF" : "#B5B7C6");
            }

            header.Text = navigation.ContainsKey(id) ? (string)navigation[id].Tag : id;
            switch (id)
            {
                case "search":
                case "library":
                case "favorites":
                    SearchPage(id);
                    break;
                case "battles":
                    BattlesPage();
                    break;
                case "compare":
                    ComparePage();
                    break;
                case "clubs":
                    ClubsPage();
                    break;
                case "rankings":
                    RankingsPage();
                    break;
                case "events":
                    EventsPage();
                    break;
                case "settings":
                    SettingsPage();
                    break;
                case "about":
                    AboutPage();
                    break;
            }

            Animate();
        }

        void Animate()
        {
            if (!options.Animations)
                return;
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(.25, 1, TimeSpan.FromMilliseconds(180)));
            var move = new TranslateTransform(0, 5);
            page.RenderTransform = move;
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(5, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        }

        async Task Run(Func<CancellationToken, Task> action, string message = null)
        {
            if (busy)
            {
                SetStatus(T("Дождитесь текущего запроса или отмените его.", "Wait for the current request or cancel it."));
                return;
            }

            busy = true;
            cancellation = new CancellationTokenSource();
            progress.Visibility = Visibility.Visible;
            cancelButton.Visibility = Visibility.Visible;
            SetStatus(message ?? T("Получаем данные…", "Fetching data…"));
            try
            {
                await action(cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                SetStatus(T("Запрос отменён. Сохранённые данные доступны.", "Request cancelled. Saved data remains available."));
            }
            catch (Exception e)
            {
                SetStatus(ErrorMessage(e));
                if (!closing)
                    ShowError(ErrorMessage(e));
            }
            finally
            {
                busy = false;
                progress.Visibility = Visibility.Collapsed;
                cancelButton.Visibility = Visibility.Collapsed;
                cancellation.Dispose();
                cancellation = null;
            }
        }

        string ErrorMessage(Exception e)
        {
            if (!L.English)
                return e.Message;
            return ErrorEnglish.Translate(e.Message);
        }

        void ShowError(string message)
        {
            var d = new Window
            {
                Owner = this,
                Title = T("Не удалось завершить действие", "Could not complete the action"),
                Width = 550,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#1A1C24"),
                ResizeMode = ResizeMode.NoResize
            };
            var p = Vertical(Text(T("Источник недоступен или запрос некорректен", "Source unavailable or request invalid"), 18, null, true), Muted(message, 13));
            p.Margin = new Thickness(24);
            p.Children[1].SetValue(MarginProperty, new Thickness(0, 16, 0, 20));
            p.Children.Add(Btn(T("Понятно", "Got it"), () => d.Close(), true));
            d.Content = p;
            d.ShowDialog();
        }

        void SearchPage(string kind)
        {
            favoriteOnly = kind == "favorites";
            localPage = 0;
            page.Children.Add(Muted(kind == "search" ? T("Ищите по нику, тегу или сохранённым признакам. Найденные профили остаются в вашей базе.", "Find names, tags or saved details. Discovered profiles stay in your local library.") : T("Источник: локальная база. Сохраняет сведения с указанием исходного сервиса.", "Source: local library. Every record keeps its original provider.")));
            query = Input(lastQuery, double.NaN);
            query.MinWidth = 200;
            query.MaxLength = 50;
            query.ToolTip = T("Ник или #ТЕГ. Локально поддерживаются * и ?.", "Name or #TAG. Local search supports * and ?. ");
            var searchRow = new Grid();
            searchRow.ColumnDefinitions.Add(new ColumnDefinition());
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchRow.Children.Add(query);
            var local = Btn(T("В моей базе", "Search library"), () =>
            {
                localPage = 0;
                RefreshResults();
            });
            local.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(local, 1);
            searchRow.Children.Add(local);
            var online = AsyncBtn(T("Найти в сети", "Search online"), () => OnlineSearch(false), true);
            online.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(online, 2);
            searchRow.Children.Add(online);
            page.Children.Add(searchRow);
            query.KeyDown += async (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    if (kind == "search")
                        await OnlineSearch(false);
                    else
                        RefreshResults();
                }
            };
            var filters = new SpacedStack
            {
                Gap = 16
            };
            minBox = Input("", 110);
            maxBox = Input("", 110);
            clubFilter = Input("", 170);
            brawlerFilter = Input("", 140);
            var years = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("any", T("Любой год", "Any year")),
                new KeyValuePair<string, string>("unknown", T("Неизвестен", "Unknown"))
            };
            for (int y = DateTime.Now.Year; y >= 2017; y--)
                years.Add(new KeyValuePair<string, string>(y.ToString(), y.ToString()));
            yearBox = Choice(years, null, 140);
            sortBox = Choice(new[] { new KeyValuePair<string, string>("Трофеи ↓", T("Трофеи ↓", "Trophies ↓")), new KeyValuePair<string, string>("Трофеи ↑", T("Трофеи ↑", "Trophies ↑")), new KeyValuePair<string, string>("Имя А–Я", T("Имя А–Я", "Name A–Z")), new KeyValuePair<string, string>("Недавно получены", T("Недавно получены", "Recently fetched")) }, null, 170);
            fromDate = new DatePicker
            {
                Width = 155
            };
            toDate = new DatePicker
            {
                Width = 155
            };
            includeUnknown = Check(T("Включать неизвестный год", "Include unknown year"));
            fuzzy = Check(T("Похожие имена", "Similar names"));
            filters.Children.Add(Fields(Field(T("Трофеи от", "Min trophies"), minBox), Field(T("Трофеи до", "Max trophies"), maxBox), Field(T("Год создания", "Creation year"), yearBox), Field(T("Сортировка", "Sort by"), sortBox), Field(T("Клуб / тег клуба", "Club / club tag"), clubFilter), Field(T("Боец", "Brawler"), brawlerFilter), Field(T("Бои с даты", "Battles from"), fromDate), Field(T("Бои по дату включительно", "Battles through"), toDate)));
            filters.Children.Add(Row(includeUnknown, fuzzy));
            filters.Children.Add(Muted(T("Год — только из сохранённых сведений с источником. Даты — по собранным боям; отсутствие совпадений не означает, что игрок не играл.", "Year uses saved evidence only. Dates search collected battles; no match does not mean the player was inactive."), 11));
            filters.Children.Add(Row(Btn(T("Применить фильтры", "Apply filters"), () =>
            {
                localPage = 0;
                RefreshResults();
            }), Btn(T("Сбросить", "Reset"), () =>
            {
                lastQuery = "";
                Navigate(kind);
            })));
            var exp = new Expander
            {
                Header = T("Фильтры · год, даты боёв, трофеи, клуб, бойцы", "Filters · year, battle dates, trophies, club, brawlers"),
                Foreground = Brush("#C5BEDC"),
                IsExpanded = true,
                Content = filters,
                Margin = new Thickness(0)
            };
            filters.Margin = new Thickness(0, 16, 0, 0);
            page.Children.Add(Card(exp));
            resultCount = Muted("");
            page.Children.Add(resultCount);
            resultList = new SpacedStack
            {
                Gap = 12
            };
            page.Children.Add(resultList);
            page.Children.Add(Row(Btn("← " + T("Назад", "Previous"), () =>
            {
                if (localPage > 0)
                {
                    localPage--;
                    RefreshResults();
                }
            }), Btn(T("Далее", "Next") + " →", () =>
            {
                if ((localPage + 1) * 40 < visiblePlayers.Count)
                {
                    localPage++;
                    RefreshResults();
                }
            }), AsyncBtn(T("Ещё из BrawlFind", "More from BrawlFind"), () => OnlineSearch(true)), Btn(T("Экспорт результатов", "Export results"), ExportResults)));
            RefreshResults();
        }

        SearchFilter ReadFilter()
        {
            int x;
            int? min = string.IsNullOrWhiteSpace(minBox.Text) ? null : int.TryParse(minBox.Text, out x) && x >= 0 ? x : throw new ArgumentException(T("Минимум трофеев должен быть неотрицательным числом.", "Minimum trophies must be a non-negative number."));
            int? max = string.IsNullOrWhiteSpace(maxBox.Text) ? null : int.TryParse(maxBox.Text, out x) && x >= 0 ? x : throw new ArgumentException(T("Максимум трофеев должен быть неотрицательным числом.", "Maximum trophies must be a non-negative number."));
            if (min > max)
                throw new ArgumentException(T("Минимум больше максимума.", "Minimum is greater than maximum."));
            if (fromDate.SelectedDate > toDate.SelectedDate)
                throw new ArgumentException(T("Начало периода позже конца.", "Start date is after the end date."));
            return new SearchFilter
            {
                Query = query.Text.Trim(),
                Min = min,
                Max = max,
                Year = int.TryParse(Value(yearBox), out x) ? x : (int? )null,
                UnknownYear = Value(yearBox) == "unknown",
                IncludeUnknown = includeUnknown.IsChecked == true,
                Fuzzy = fuzzy.IsChecked == true,
                Favorites = favoriteOnly,
                Club = clubFilter.Text,
                Brawler = brawlerFilter.Text,
                From = fromDate.SelectedDate?.Date.ToUniversalTime(),
                Until = toDate.SelectedDate?.Date.AddDays(1).ToUniversalTime(),
                Sort = Value(sortBox)
            };
        }

        void RefreshResults()
        {
            try
            {
                lastQuery = query.Text;
                var f = ReadFilter();
                visiblePlayers = f.Apply(db.Players.FindAll(), db.Battles.FindAll()).ToList();
                resultCount.Text = T("Найдено: ", "Found: ") + visiblePlayers.Count + "  ·  " + T("В базе: ", "In library: ") + db.Players.Count() + "  ·  " + T("Страница ", "Page ") + (localPage + 1);
                resultList.Children.Clear();
                foreach (var p in visiblePlayers.Skip(localPage * 40).Take(40))
                    resultList.Children.Add(PlayerRow(p));
                if (visiblePlayers.Count == 0)
                {
                    var empty = Vertical(Text(T("Здесь появятся найденные игроки", "Your discoveries appear here"), 20, "#CDC7E2", true), Muted(T("Введите ник для поиска в BrawlFind или полный тег для подробного профиля. Можно начать с #8LQ9JR82.", "Search a name in BrawlFind or enter a full tag for a detailed profile. Try #8LQ9JR82.")));
                    empty.Margin = new Thickness(10, 20, 10, 26);
                    resultList.Children.Add(Card(empty));
                }
            }
            catch (Exception e)
            {
                SetStatus(ErrorMessage(e));
            }
        }

        async Task OnlineSearch(bool next)
        {
            await Run(async ct =>
            {
                var f = ReadFilter();
                var q = query.Text.Trim();
                if (q.Length == 0)
                    throw new ArgumentException(T("Введите ник или тег.", "Enter a name or tag."));
                lastQuery = q;
                if (q.StartsWith("#") && Util.ValidTag(q) && !q.Contains("*") && !q.Contains("?"))
                {
                    await FetchProfile(q, ct, true);
                    return;
                }

                if (!next)
                    remotePage = 1;
                else
                    remotePage++;
                var found = await sources.Search(q, remotePage, ct, f.Min ?? 0, f.Max ?? 200000);
                foreach (var p in found)
                    db.Save(p);
                if (current == "search" || current == "library" || current == "favorites")
                {
                    localPage = 0;
                    RefreshResults();
                }

                SetStatus(T("BrawlFind: получено ", "BrawlFind: received ") + found.Count + T(" профилей. Дополнительные фильтры применены локально.", " profiles. Additional filters were applied locally."));
            });
        }

        UIElement PlayerRow(Player p)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var monogram = Text(string.IsNullOrEmpty(p.Name) ? "?" : StringInfo.GetNextTextElement(p.Name), 20, "#D9D1FF", true);
            monogram.HorizontalAlignment = HorizontalAlignment.Center;
            monogram.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(new Border { Background = Brush("#383247"), CornerRadius = new CornerRadius(11), Width = 40, Height = 40, Child = monogram, VerticalAlignment = VerticalAlignment.Center });
            var name = Btn(p.Name, () => OpenProfile(p.Tag));
            name.MinHeight = 0;
            name.Padding = new Thickness(0);
            name.Background = Brush("#001A1C24");
            name.BorderThickness = new Thickness(0);
            name.HorizontalContentAlignment = HorizontalAlignment.Left;
            name.FontWeight = FontWeights.SemiBold;
            name.FontSize = 15;
            var information = Vertical(name, Muted(p.Tag + "  ·  " + (string.IsNullOrEmpty(p.ClubName) ? T("Клуб не указан", "Club not specified") : p.ClubName), 11), Muted(L.Source(p.Source) + "  ·  " + Util.Stamp(p.FetchedAt) + (p.Year.HasValue ? "  ·  " + p.Year + " (" + L.Source(p.YearSource) + ")" : ""), 10));
            information.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(information, 1);
            grid.Children.Add(information);
            var trophies = Vertical(Text(Util.Num(p.Trophies), 18, "#E9D6A0", true), Muted(T("трофеев", "trophies"), 10));
            trophies.Margin = new Thickness(12, 0, 10, 0);
            Grid.SetColumn(trophies, 2);
            grid.Children.Add(trophies);
            var save = Btn("", () =>
            {
                p.Favorite = !p.Favorite;
                db.Players.Update(p);
                if (resultList != null && (current == "search" || current == "library" || current == "favorites"))
                    RefreshResults();
            });
            save.Content = Branding.Icon("favorites");
            save.ToolTip = p.Favorite ? T("Убрать из избранного", "Remove from saved") : T("В избранное", "Save player");
            save.Background = Brush(p.Favorite ? "#443956" : "#282B36");
            var compareButton = Btn("", () =>
            {
                ToggleCompare(p.Tag);
                SetStatus(T("В сравнении: ", "Players in comparison: ") + compare.Count);
            });
            compareButton.Content = Branding.Icon("compare");
            compareButton.ToolTip = T("Добавить / убрать из сравнения", "Add / remove from comparison");
            foreach (var b in new[]
            {
                save,
                compareButton
            }

            )
            {
                b.Width = 44;
                b.Height = 44;
                b.Padding = new Thickness(9);
                System.Windows.Automation.AutomationProperties.SetName(b, (string)b.ToolTip);
            }

            var actions = Row(save, compareButton);
            actions.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(actions, 3);
            grid.Children.Add(actions);
            return Card(grid, 16);
        }

        void ToggleCompare(string tag)
        {
            if (compare.Contains(tag))
                compare.Remove(tag);
            else if (compare.Count < 4)
                compare.Add(tag);
            else
                SetStatus(T("Можно сравнить до четырёх игроков.", "You can compare up to four players."));
        }

        void OpenProfile(string tag)
        {
            tag = Util.Tag(tag);
            var p = db.Players.FindById(tag);
            if (p != null && p.Detailed)
            {
                ProfilePage(p);
                return;
            }

            _ = Run(ct => FetchProfile(tag, ct, true));
        }

        async Task FetchProfile(string tag, CancellationToken ct, bool show)
        {
            var r = await sources.Profile(tag, ct);
            ct.ThrowIfCancellationRequested();
            var p = db.Save(r.Player);
            var count = db.AddBattles(r.Battles);
            foreach (var h in r.History)
                db.Snapshots.Upsert(h);
            if (show && !closing)
                ProfilePage(p);
            SetStatus(T("Сохранено боёв: ", "New archived battles: ") + count + " · " + L.Source(p.Source) + ". " + ErrorEnglishIfNeeded(r.Notice));
        }

        string ErrorEnglishIfNeeded(string s) => L.English ? ErrorEnglish.Translate(s) : s.Replace("Brawl Time Ninja unavailable; BrawlFind used.", "Brawl Time Ninja недоступен; использован BrawlFind.");
        Border Metric(string caption, string value, string source = null)
        {
            var p = Vertical(Muted(caption, 11), Text(value, 25, null, true));
            if (source != null)
                p.Children.Add(Muted(source, 10));
            var card = Card(p, 16);
            card.MinWidth = 145;
            card.Margin = new Thickness(0);
            return card;
        }

        void ProfilePage(Player p)
        {
            selected = p;
            current = "profile";
            page.Children.Clear();
            header.Text = T("Профиль игрока", "Player profile");
            var title = Text(p.Name, 31, null, true);
            page.Children.Add(title);
            page.Children.Add(Muted(p.Tag + "   ·   " + L.Source(p.Source) + "   ·   " + T("Получено ", "Fetched ") + Util.Stamp(p.FetchedAt)));
            var tools = Row(AsyncBtn(T("Обновить", "Refresh"), () => Run(ct => FetchProfile(p.Tag, ct, true)), true), Btn(p.Favorite ? T("★ В избранном", "★ Saved") : T("☆ Сохранить", "☆ Save"), () =>
            {
                p.Favorite = !p.Favorite;
                db.Players.Update(p);
                ProfilePage(p);
            }), Btn(T("⇄ Сравнить", "⇄ Compare"), () =>
            {
                if (!compare.Contains(p.Tag))
                    ToggleCompare(p.Tag);
                Navigate("compare");
            }), Btn(T("Копировать тег", "Copy tag"), () => Copy(p.Tag)), Btn(T("Экспорт JSON", "Export JSON"), () => ExportProfile(p)));
            page.Children.Add(tools);
            page.Children.Add(Fields(Metric(T("Трофеи", "Trophies"), Util.Num(p.Trophies)), Metric(T("Рекорд", "Highest trophies"), Util.Num(p.HighestTrophies)), Metric(T("Бойцы", "Brawlers"), p.Brawlers.Count.ToString()), Metric(T("Уровень опыта", "Experience level"), Util.Num(p.Level))));
            page.Children.Add(Fields(Metric(T("Победы 3×3", "3v3 victories"), Util.Num(p.Wins3)), Metric(T("Соло", "Solo victories"), Util.Num(p.WinsSolo)), Metric(T("Дуо", "Duo victories"), Util.Num(p.WinsDuo))));
            var facts = Vertical(Text(T("Сведения об аккаунте", "Account details"), 17, null, true));
            facts.Children.Add(Fact(T("Клуб", "Club"), string.IsNullOrEmpty(p.ClubName) ? "—" : p.ClubName + "  " + p.ClubTag, L.Source(p.Source)));
            var battles = db.ForPlayer(p.Tag);
            facts.Children.Add(Fact(T("Последний обнаруженный бой", "Latest observed battle"), battles.Count > 0 ? Util.Stamp(battles[0].Time) : "—", battles.Count > 0 ? L.Source(battles[0].Source) : T("Архив пуст", "Archive is empty")));
            facts.Children.Add(Fact(T("Год создания", "Creation year"), p.Year?.ToString() ?? T("Неизвестен", "Unknown"), p.Year.HasValue ? L.Source(p.YearSource) : T("Источник не предоставляет дату регистрации", "Provider does not expose the creation date")));
            facts.Children.Add(Fact(T("В нашей базе с", "In this library since"), Util.Stamp(p.FirstSeen), T("Локальная запись; не дата регистрации", "Local record; not the creation date")));
            if (p.Aliases.Count > 0)
                facts.Children.Add(Fact(T("Замеченные имена", "Observed names"), string.Join(" · ", p.Aliases), T("Локальная история наблюдений", "Local observation history")));
            var raw = JObject.Parse(p.Raw ?? "{}");
            foreach (var key in new[]
            {
                "rankedRankName",
                "rankedElo",
                "highestAllTimeRankedRankName",
                "highestAllTimeRankedElo",
                "totalPrestigeLevel",
                "fameTierName"
            }

            )
                if (raw[key] != null)
                    facts.Children.Add(Fact(ExtraCaption(key), Util.Str(raw[key]), L.Source(p.Source)));
            facts.Children.Add(Muted(T("Время боя не означает последний вход. Показатели сервиса могут обновляться с задержкой.", "A battle timestamp is not the last login. Provider data may be delayed."), 11));
            page.Children.Add(Card(facts));
            var links = Row(Btn("Brawl Time Ninja ↗", () => Link("https://brawltime.ninja/profile/" + p.Tag.TrimStart('#'))), Btn("BrawlFind ↗", () => Link("https://www.brawlfind.com/player/" + p.Tag.TrimStart('#'))), Btn("Brawlify ↗", () => Link("https://brawlify.com/player/" + p.Tag.TrimStart('#'))));
            page.Children.Add(links);
            page.Children.Add(Row(AsyncBtn(T("Дополнить историю из BrawlFind", "Add BrawlFind trophy history"), () => Run(async ct =>
            {
                var r = await sources.FindProfile(p.Tag, ct);
                foreach (var h in r.History)
                    db.Snapshots.Upsert(h);
                ProfilePage(p);
                SetStatus(T("Точек истории из BrawlFind: ", "BrawlFind history points: ") + r.History.Count);
            })), Btn(T("Настроить источники", "Configure sources"), () => Navigate("settings"))));
            page.Children.Add(HistoryChart(p));
            var bt = Vertical(Text(T("Бои и статистика", "Battles and performance"), 18, null, true));
            int wins = battles.Count(b => b.Result == "victory"), losses = battles.Count(b => b.Result == "defeat"), draws = battles.Count(b => b.Result == "draw");
            bt.Children.Add(Muted(T("Источник: локальный архив с исходными сервисами. Только собранные матчи.", "Source: local archive with original providers. Collected matches only.")));
            bt.Children.Add(Fields(Metric(T("Собрано боёв", "Archived battles"), battles.Count.ToString()), Metric(T("Победы / поражения", "Wins / losses"), wins + " / " + losses), Metric(T("Винрейт без ничьих", "Win rate, draws excluded"), wins + losses > 0 ? (100.0 * wins / (wins + losses)).ToString("0.0") + "%" : "—")));
            if (draws > 0)
                bt.Children.Add(Muted(T("Ничьих: ", "Draws: ") + draws));
            bt.Children.Add(Btn(T("Открыть бои этого игрока", "View this player's battles"), () =>
            {
                current = "battles";
                page.Children.Clear();
                header.Text = T("Архив боёв", "Battle archive");
                BattlesPage(p.Tag);
            }));
            page.Children.Add(Card(bt));
            var brawlerHeader = Row(Text(T("Бойцы", "Brawlers"), 19, null, true), Muted(L.Source(p.Source)));
            page.Children.Add(brawlerHeader);
            var bQuery = Input("", 230);
            var bSort = Choice(new[] { T("Трофеи ↓", "Trophies ↓"), T("Сила ↓", "Power ↓"), T("Имя А–Я", "Name A–Z") }, 170);
            page.Children.Add(Row(Field(T("Найти бойца", "Find a brawler"), bQuery), Field(T("Сортировка", "Sort by"), bSort)));
            var fighterPanel = new GapRow();
            page.Children.Add(fighterPanel);
            Action renderFighters = () =>
            {
                fighterPanel.Children.Clear();
                IEnumerable<Fighter> list = p.Brawlers.Where(b => Util.Match(b.Name, bQuery.Text));
                list = bSort.SelectedIndex == 1 ? list.OrderByDescending(b => b.Power) : bSort.SelectedIndex == 2 ? list.OrderBy(b => b.Name) : list.OrderByDescending(b => b.Trophies);
                foreach (var b in list)
                {
                    var content = Vertical(Text(b.Name, 15, null, true), Text(Util.Num(b.Trophies) + "  /  " + Util.Num(b.HighestTrophies), 18, "#E9D6A0"), Muted(T("Сила ", "Power ") + Util.Num(b.Power)), Muted(T("Гаджеты: ", "Gadgets: ") + Nonempty(b.Gadgets), 10), Muted(T("Звёздные силы: ", "Star powers: ") + Nonempty(b.StarPowers), 10), Muted(T("Снаряжение: ", "Gears: ") + Nonempty(b.Gears), 10));
                    var card = Card(content, 15);
                    card.Width = 250;
                    card.ToolTip = L.Source(p.Source);
                    fighterPanel.Children.Add(card);
                }
            };
            bQuery.TextChanged += (s, e) => renderFighters();
            bSort.SelectionChanged += (s, e) => renderFighters();
            renderFighters();
            var notes = Vertical(Text(T("Мои сведения", "My evidence"), 18, null, true), Muted(T("Отдельно от данных сервисов. Год не вычисляется по тегу.", "Separate from provider data. The year is not inferred from the tag.")));
            var year = Input(p.Year?.ToString() ?? "", 110);
            var evidence = Input(p.YearSource, 350);
            var note = Input(p.Notes, double.NaN);
            note.AcceptsReturn = true;
            note.TextWrapping = TextWrapping.Wrap;
            note.MinHeight = 80;
            note.MaxLength = 10000;
            var watch = Check(T("Обновлять при открытом приложении", "Refresh while the app is running"), p.Watch);
            notes.Children.Add(Fields(Field(T("Год", "Year"), year), Field(T("Источник года / подтверждение", "Year source / evidence"), evidence)));
            notes.Children.Add(Field(T("Заметка", "Note"), note));
            notes.Children.Add(watch);
            notes.Children.Add(Btn(T("Сохранить сведения", "Save evidence"), () =>
            {
                int y;
                if (year.Text.Trim().Length > 0 && (!int.TryParse(year.Text, out y) || y < 2017 || y > DateTime.Now.Year))
                {
                    SetStatus(T("Год должен быть между 2017 и текущим годом.", "Year must be between 2017 and the current year."));
                    return;
                }

                p.Year = int.TryParse(year.Text, out y) ? y : (int? )null;
                p.YearSource = p.Year.HasValue ? T("Вручную: ", "Manual: ") + (string.IsNullOrWhiteSpace(evidence.Text) ? T("пользователь", "user") : evidence.Text.Replace("Вручную: ", "").Replace("Manual: ", "")) : "";
                p.Notes = note.Text;
                p.Watch = watch.IsChecked == true;
                db.Players.Update(p);
                SetStatus(T("Сведения сохранены локально.", "Evidence saved locally."));
            }));
            page.Children.Add(Card(notes));
            var rawButton = Btn(T("Все исходные поля JSON", "All raw JSON fields"), () => JsonDialog(p.Raw, L.Source(p.Source)));
            page.Children.Add(rawButton);
            Animate();
        }

        string Nonempty(string s) => string.IsNullOrEmpty(s) ? "—" : s;
        string ExtraCaption(string key)
        {
            switch (key)
            {
                case "rankedRankName":
                    return T("Текущий ранг", "Current ranked tier");
                case "rankedElo":
                    return T("Рейтинговые очки", "Ranked points");
                case "highestAllTimeRankedRankName":
                    return T("Лучший ранг", "Best ranked tier");
                case "highestAllTimeRankedElo":
                    return T("Рекорд рейтинговых очков", "Highest ranked points");
                case "totalPrestigeLevel":
                    return T("Престиж", "Prestige");
                case "fameTierName":
                    return T("Слава", "Fame");
                default:
                    return key;
            }
        }

        UIElement Fact(string label, string value, string source)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(Muted(label));
            var p = Vertical(Text(value), Muted(source, 10));
            Grid.SetColumn(p, 1);
            grid.Children.Add(p);
            return grid;
        }

        UIElement HistoryChart(Player p)
        {
            var list = db.Snapshots.Find(s => s.Tag == p.Tag).OrderBy(s => s.Time).ToList();
            var panel = Vertical(Text(T("История наблюдений", "Observation history"), 18, null, true), Muted(T("Источники указаны у точек: снимки приложения или история BrawlFind. Промежутки между точками не наблюдались.", "Each point shows its source: app snapshots or BrawlFind history. Gaps between points were not observed.")));
            var points = list.Where(s => s.Trophies.HasValue).ToList();
            if (points.Count < 2)
                panel.Children.Add(Muted(T("Для графика нужно минимум два снимка с изменением трофеев. Они появятся при обновлении профиля.", "The chart needs at least two snapshots with a trophy change. Refresh this profile over time.")));
            else
            {
                var canvas = new Canvas
                {
                    Height = 150,
                    Margin = new Thickness(0, 20, 0, 10),
                    ClipToBounds = true
                };
                panel.Children.Add(canvas);
                Action draw = () =>
                {
                    canvas.Children.Clear();
                    double width = Math.Max(100, canvas.ActualWidth - 30), min = points.Min(s => s.Trophies.Value), range = Math.Max(1, points.Max(s => s.Trophies.Value) - min), time = Math.Max(1, (points.Last().Time - points.First().Time).TotalSeconds);
                    var line = new System.Windows.Shapes.Polyline
                    {
                        Stroke = Brush("#B9ADFF"),
                        StrokeThickness = 2.5
                    };
                    foreach (var s in points)
                    {
                        double x = 15 + (s.Time - points.First().Time).TotalSeconds / time * width, y = 125 - (s.Trophies.Value - min) / range * 105;
                        line.Points.Add(new Point(x, y));
                        var dot = new Ellipse
                        {
                            Width = 6,
                            Height = 6,
                            Fill = Brush("#D8D0FF"),
                            ToolTip = Util.Stamp(s.Time) + " · " + Util.Num(s.Trophies) + " · " + L.Source(s.Source)
                        };
                        Canvas.SetLeft(dot, x - 3);
                        Canvas.SetTop(dot, y - 3);
                        canvas.Children.Add(dot);
                    }

                    canvas.Children.Insert(0, line);
                };
                canvas.SizeChanged += (s, e) => draw();
            }

            foreach (var s in list.OrderByDescending(s => s.Time).Take(6))
                panel.Children.Add(Muted(Util.Stamp(s.Time) + "  ·  " + Util.Num(s.Trophies) + "  ·  " + s.Name + "  ·  " + s.Club + "  ·  " + L.Source(s.Source), 11));
            return Card(panel);
        }

        void BattlesPage(string owner = "")
        {
            page.Children.Add(Muted(T("Источник: локально сохранённые журналы. Даты — в часовом поясе Windows. Старые бои доступны только если источник или приложение их сохранили.", "Source: locally archived logs. Dates use your Windows time zone. Older battles exist only when a provider or this app saved them.")));
            var who = Input(owner, 185);
            var opponent = Input("", 200);
            var start = new DatePicker
            {
                Width = 160
            };
            var end = new DatePicker
            {
                Width = 160
            };
            var modes = new List<string>
            {
                T("Все режимы", "All modes")
            };
            modes.AddRange(db.Battles.FindAll().Select(b => b.Mode).Distinct().OrderBy(s => s));
            var mode = Choice(modes.ToArray(), 180);
            var results = Choice(new[] { new KeyValuePair<string, string>("", T("Все результаты", "All results")), new KeyValuePair<string, string>("victory", T("Победа", "Victory")), new KeyValuePair<string, string>("defeat", T("Поражение", "Defeat")), new KeyValuePair<string, string>("draw", T("Ничья", "Draw")) }, null, 150);
            var map = Input("", 170);
            var fighter = Input("", 150);
            page.Children.Add(Card(Fields(Field(T("Журнал игрока (#тег)", "Player's log (#tag)"), who), Field(T("Участник: имя / тег", "Participant: name / tag"), opponent), Field(T("С даты", "From date"), start), Field(T("По дату включительно", "Through date"), end), Field(T("Режим", "Mode"), mode), Field(T("Результат владельца журнала", "Log owner's result"), results), Field(T("Карта", "Map"), map), Field(T("Любой боец в матче", "Any brawler in match"), fighter))));
            var summary = Muted("");
            var list = new SpacedStack
            {
                Gap = 12
            };
            int limit = 40;
            List<Battle> filtered = new List<Battle>();
            Action render = () =>
            {
                list.Children.Clear();
                if (start.SelectedDate > end.SelectedDate)
                {
                    summary.Text = T("Начало периода позже конца.", "Start date is after the end date.");
                    return;
                }

                DateTime? a = start.SelectedDate?.Date.ToUniversalTime(), z = end.SelectedDate?.Date.AddDays(1).ToUniversalTime();
                filtered = db.Battles.FindAll().Where(b => (string.IsNullOrWhiteSpace(who.Text) || b.Owner == Util.Tag(who.Text)) && (!a.HasValue || b.Time >= a) && (!z.HasValue || b.Time < z) && (mode.SelectedIndex == 0 || b.Mode == Value(mode)) && (Value(results) == "" || b.Result == Value(results)) && Util.Match(b.Map, map.Text) && (string.IsNullOrWhiteSpace(opponent.Text) || b.Players.Any(p => Util.Match(p.Tag + " " + p.Name, opponent.Text))) && (string.IsNullOrWhiteSpace(fighter.Text) || b.Players.Any(p => Util.Match(p.Brawler, fighter.Text)))).OrderByDescending(b => b.Time).ToList();
                summary.Text = T("Записей: ", "Records: ") + filtered.Count + "  ·  " + T("Результат относится к владельцу журнала; один матч может присутствовать в нескольких журналах.", "Results belong to the log owner; one match can appear in several logs.");
                foreach (var b in filtered.Take(limit))
                    list.Children.Add(BattleCard(b));
                if (filtered.Count == 0)
                    list.Children.Add(Card(Muted(T("Нет сохранённых боёв с такими условиями. Откройте профиль игрока и обновите его.", "No saved battles match these filters. Open and refresh a player profile."))));
            };
            page.Children.Add(Row(Btn(T("Применить", "Apply"), () =>
            {
                limit = 40;
                render();
            }, true), Btn(T("Экспорт JSON", "Export JSON"), () => SaveJson(filtered, "ST4RR-battles.json")), Btn(T("Показать ещё", "Show more"), () =>
            {
                limit += 40;
                render();
            })));
            page.Children.Add(summary);
            page.Children.Add(list);
            render();
        }

        UIElement BattleCard(Battle b)
        {
            var result = b.Result == "victory" ? T("ПОБЕДА", "VICTORY") : b.Result == "defeat" ? T("ПОРАЖЕНИЕ", "DEFEAT") : b.Result == "draw" ? T("НИЧЬЯ", "DRAW") : b.Rank.HasValue ? T("МЕСТО ", "RANK ") + b.Rank : T("НЕИЗВЕСТНО", "UNKNOWN");
            string color = b.Result == "victory" ? "#9ED7BC" : b.Result == "defeat" ? "#E1A2AB" : "#BBB5CD";
            var panel = Vertical();
            panel.Children.Add(Row(Text(result, 12, color, true), Text(L.English ? b.Mode : Util.Mode(b.Mode), 15, null, true), Muted(b.Map), Text(b.Change.HasValue ? b.Change.Value.ToString("+0;-0;0") : "—", 14, "#E4CF9C")));
            panel.Children.Add(Muted(Util.Stamp(b.Time) + "  ·  " + L.Source(b.Source) + "  ·  " + T("Журнал ", "Log ") + b.Owner + "  ·  " + b.Type));
            var players = new GapRow();
            foreach (var p in b.Players)
            {
                var btn = Btn(p.Name + " · " + p.Brawler, () => OpenProfile(p.Tag));
                btn.FontSize = 11;
                btn.Padding = new Thickness(9, 6, 9, 6);
                btn.ToolTip = p.Tag + " · " + T("Команда ", "Team ") + (p.Team + 1) + " · " + Util.Num(p.Trophies);
                players.Children.Add(btn);
            }

            panel.Children.Add(players);
            panel.Children.Add(Btn(T("Детали JSON", "JSON details"), () => JsonDialog(b.Raw, L.Source(b.Source))));
            return Card(panel, 16);
        }

        void ComparePage()
        {
            page.Children.Add(Muted(T("Источники и время получения указаны для каждого игрока. Добавьте до четырёх профилей.", "Source and fetch time are shown for each player. Add up to four profiles.")));
            var tag = Input("", 220);
            page.Children.Add(Row(tag, AsyncBtn(T("Добавить тег", "Add tag"), () => Run(async ct =>
            {
                var normalized = Util.Tag(tag.Text);
                if (!Util.ValidTag(normalized))
                    throw new ArgumentException(T("Некорректный тег", "Invalid tag"));
                var p = db.Players.FindById(normalized);
                if (p == null || !p.Detailed)
                    await FetchProfile(normalized, ct, false);
                if (!compare.Contains(normalized))
                    ToggleCompare(normalized);
                Navigate("compare");
            }), true), Btn(T("Очистить", "Clear"), () =>
            {
                compare.Clear();
                Navigate("compare");
            })));
            var wrap = new GapRow();
            foreach (var t in compare.ToList())
            {
                var p = db.Players.FindById(t);
                if (p == null)
                    continue;
                var col = Vertical(Text(p.Name, 20, null, true), Muted(p.Tag), Muted(L.Source(p.Source) + "\n" + Util.Stamp(p.FetchedAt), 11));
                foreach (var metric in new[]
                {
                    new[]
                    {
                        T("Трофеи", "Trophies"),
                        Util.Num(p.Trophies)
                    },
                    new[]
                    {
                        T("Рекорд", "Highest trophies"),
                        Util.Num(p.HighestTrophies)
                    },
                    new[]
                    {
                        T("Бойцы", "Brawlers"),
                        p.Detailed ? p.Brawlers.Count.ToString() : "—"
                    },
                    new[]
                    {
                        T("Уровень", "Level"),
                        Util.Num(p.Level)
                    },
                    new[]
                    {
                        T("Победы 3×3", "3v3 victories"),
                        Util.Num(p.Wins3)
                    },
                    new[]
                    {
                        T("Соло", "Solo victories"),
                        Util.Num(p.WinsSolo)
                    },
                    new[]
                    {
                        T("Дуо", "Duo victories"),
                        Util.Num(p.WinsDuo)
                    },
                    new[]
                    {
                        T("Год создания", "Creation year"),
                        p.Year?.ToString() ?? "—"
                    }
                }

                )
                {
                    col.Children.Add(Vertical(Muted(metric[0]), Text(metric[1], 23)));
                }

                col.Children.Add(Muted(L.Source(p.YearSource), 10));
                col.Children.Add(Btn(T("Открыть профиль", "Open profile"), () => OpenProfile(t)));
                col.Children.Add(Btn(T("Убрать", "Remove"), () =>
                {
                    compare.Remove(t);
                    Navigate("compare");
                }));
                var card = Card(col, 18);
                card.Width = 205;
                wrap.Children.Add(card);
            }

            page.Children.Add(wrap);
            if (compare.Count == 0)
                page.Children.Add(Card(Muted(T("Нажмите ⇄ у найденного игрока или введите тег выше.", "Click ⇄ beside a discovered player or enter a tag above."))));
        }

        void ClubsPage()
        {
            page.Children.Add(Muted(T("Состав клуба: официальный API при подключении или публичная страница BrawlFind.", "Club members: official API when connected, otherwise BrawlFind's public page.")));
            var tag = Input("", 230);
            var area = new SpacedStack
            {
                Gap = 16
            };
            page.Children.Add(Row(Field(T("Тег клуба", "Club tag"), tag), AsyncBtn(T("Открыть клуб", "Open club"), () => Run(async ct =>
            {
                var j = await sources.Club(tag.Text, ct);
                area.Children.Clear();
                var src = Util.Str(j["source"]);
                if (src.Length == 0)
                    src = "Supercell API";
                area.Children.Add(Text(Util.Str(j["name"]), 25, null, true));
                area.Children.Add(Muted(src + " · " + Util.Stamp(DateTime.UtcNow)));
                if (j["description"] != null)
                    area.Children.Add(Text(Util.Str(j["description"])));
                foreach (var x in j["members"] as JArray ?? new JArray())
                {
                    var p = new Player
                    {
                        Tag = Util.Tag(Util.Str(x["tag"])),
                        Name = Util.Str(x["name"]),
                        Trophies = Util.Int(x["trophies"]),
                        ClubTag = Util.Tag(tag.Text),
                        ClubName = Util.Str(j["name"]),
                        Source = src + " • " + T("состав клуба", "club roster"),
                        FetchedAt = DateTime.UtcNow
                    };
                    if (Util.ValidTag(p.Tag))
                        area.Children.Add(PlayerRow(db.Save(p)));
                }

                area.Children.Add(Btn(T("Исходный JSON", "Raw JSON"), () => JsonDialog(j.ToString(), src)));
                SetStatus(T("Состав клуба сохранён в базу.", "Club roster saved to the library."));
            }), true)));
            page.Children.Add(area);
        }

        void RankingsPage()
        {
            page.Children.Add(Muted(T("Без ключа — публичная подборка BrawlFind. С официальным API — до 200 лидеров выбранного региона.", "Without a key: BrawlFind's public selection. With the official API: up to 200 regional leaders.")));
            var region = Choice(new[] { "global", "RU", "US", "DE", "FR", "TR", "BR", "JP", "KR" }, 160);
            var area = new SpacedStack
            {
                Gap = 16
            };
            page.Children.Add(Row(region, AsyncBtn(T("Загрузить игроков", "Load players"), () => Run(async ct =>
            {
                var players = await sources.Ranking(Value(region), ct);
                area.Children.Clear();
                foreach (var p in players)
                    area.Children.Add(PlayerRow(db.Save(p)));
                SetStatus(T("Получено игроков: ", "Players received: ") + players.Count);
                if (players.Count == 0)
                    area.Children.Add(Muted(T("Источник не предоставил список. Используйте поиск по нику.", "The provider did not return a list. Try searching by name.")));
            }), true)));
            page.Children.Add(area);
        }

        void EventsPage()
        {
            page.Children.Add(Muted(T("Источник: Supercell API при подключении, иначе публичная ротация Brawl Time Ninja. Время указано сервисом.", "Source: Supercell API when connected, otherwise Brawl Time Ninja's public rotation. Times are supplied by the provider.")));
            var area = new SpacedStack
            {
                Gap = 16
            };
            page.Children.Add(AsyncBtn(T("Получить события", "Fetch events"), () => Run(async ct =>
            {
                var j = await sources.Events(ct);
                area.Children.Clear();
                string src = options.OfficialEnabled && !string.IsNullOrEmpty(options.Key) ? "Supercell API" : "Brawl Time Ninja";
                IEnumerable<JToken> items = j is JArray arr ? arr : (j["current"] as JArray ?? new JArray()).Concat(j["upcoming"] as JArray ?? new JArray());
                foreach (var item in items)
                {
                    var ev = item["event"] ?? item;
                    var map = ev["map"];
                    var title = map is JObject ? Util.Str(map["name"]) : Util.Str(map);
                    var mode = Util.Str(ev["mode"] ?? item["mode"]);
                    var card = Vertical(Text(string.IsNullOrEmpty(title) ? T("Событие", "Event") : title, 19, null, true), Muted(mode), Muted(src + " · " + T("Получено ", "Fetched ") + Util.Stamp(DateTime.UtcNow)));
                    foreach (var key in new[]
                    {
                        "startTime",
                        "endTime"
                    }

                    )
                        if (item[key] != null)
                            card.Children.Add(Muted(key + ": " + Util.Str(item[key])));
                    area.Children.Add(Card(card));
                }

                area.Children.Add(Btn(T("Все поля JSON", "All JSON fields"), () => JsonDialog(j.ToString(), src)));
                SetStatus(T("События получены.", "Events fetched."));
            }), true));
            page.Children.Add(area);
        }

        async Task Track()
        {
            await Run(async ct =>
            {
                int count = 0;
                foreach (var p in db.Players.FindAll().Where(p => p.Watch).Take(30).ToList())
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        await FetchProfile(p.Tag, ct, false);
                        count++;
                    }
                    catch (Exception e)when (!(e is OperationCanceledException))
                    {
                        SetStatus(ErrorMessage(e));
                    }

                    await Task.Delay(1000, ct);
                }

                SetStatus(T("Обновлено отслеживаемых профилей: ", "Tracked profiles refreshed: ") + count);
            });
        }

        void SettingsPage()
        {
            var language = Choice(new[] { new KeyValuePair<string, string>("ru", "Русский"), new KeyValuePair<string, string>("en", "English") }, L.English ? "en" : "ru", 240);
            page.Children.Add(Card(Vertical(Text(T("Язык", "Language"), 18, null, true), Field(T("Язык интерфейса", "Interface language"), language))));
            var ninja = Check("Brawl Time Ninja", options.NinjaEnabled);
            var find = Check("BrawlFind", options.FindEnabled);
            var official = Check(T("Официальный API Supercell (необязательно)", "Official Supercell API (optional)"), options.OfficialEnabled);
            var preferred = Choice(new[] { new KeyValuePair<string, string>("auto", T("Автоматически", "Automatic")), new KeyValuePair<string, string>("ninja", "Brawl Time Ninja"), new KeyValuePair<string, string>("find", "BrawlFind"), new KeyValuePair<string, string>("official", "Supercell API") }, options.PreferredSource, 230);
            var providers = Vertical(Text(T("Источники данных", "Data sources"), 18, null, true), Muted(T("Поиск работает без регистрации. Можно отключить любой внешний источник; локальная база остаётся доступной.", "Search works without signing up. Disable any external provider; your local library remains available.")), ninja, Muted(T("Публичные профили, бойцы, доступные бои и события. Данные могут быть кэшированы сервисом.", "Public profiles, brawlers, available battles and events. Provider data may be cached.")), find, Muted(T("Поиск по началу ника / тега, подборка игроков и составы клубов. Охват определяется индексом BrawlFind.", "Name / tag prefix search, featured players and club rosters. Coverage depends on BrawlFind's index.")), official, Muted(T("Прямые запросы к Supercell. Ключ привязан к разрешённому внешнему IP и хранится зашифрованным для пользователя Windows.", "Direct Supercell requests. The key is bound to an allowed public IP and encrypted for this Windows user.")));
            providers.Children.Add(Field(T("Предпочтительный источник профиля", "Preferred profile provider"), preferred));
            providers.Children.Add(Muted(T("Если выбранный источник недоступен, используется другой включённый источник. BrawlFind предоставляет историю трофеев; бои загружаются из Brawl Time Ninja или API.", "If the preferred provider is unavailable, another enabled provider is used. BrawlFind supplies trophy history; battles come from Brawl Time Ninja or the API."), 11));
            var key = new PasswordBox
            {
                Password = options.Key,
                MinHeight = 44,
                Width = 460
            };
            providers.Children.Add(Field(T("API-ключ (можно оставить пустым)", "API key (leave blank to use public sources)"), key));
            providers.Children.Add(Row(Btn(T("Создать ключ ↗", "Create a key ↗"), () => Link("https://developer.brawlstars.com")), AsyncBtn(T("Проверить API", "Test API"), () => Run(async ct =>
            {
                if (string.IsNullOrWhiteSpace(key.Password))
                    throw new ArgumentException(T("Ключ не введён. Для обычной работы он не нужен.", "No key entered. A key is not required for normal use."));
                await sources.Http.Get("https://api.brawlstars.com/v1/brawlers", ct, key.Password.Trim());
                SetStatus(T("Официальный API отвечает успешно.", "Official API connection succeeded."));
            }))));
            providers.Children.Add(Muted(T("Brawlify: внешние ссылки из профиля; автоматическое получение отключено, поскольку сервис отклоняет прямые запросы. Источники не объединяются с потерей происхождения.", "Brawlify: external profile links; automated fetching is unavailable because the service rejects direct requests. Data retains its provider attribution."), 11));
            page.Children.Add(Card(providers));
            var auto = Check(T("Автоматически обновлять отмеченные профили", "Automatically refresh tracked profiles"), options.AutoRefresh);
            var interval = Choice(new[] { "5", "15", "30", "60" }, 120);
            interval.SelectedIndex = options.IntervalMinutes == 5 ? 0 : options.IntervalMinutes == 30 ? 2 : options.IntervalMinutes == 60 ? 3 : 1;
            page.Children.Add(Card(Vertical(Text(T("Наблюдение", "Tracking"), 18, null, true), auto, Field(T("Интервал, минуты", "Interval, minutes"), interval), Muted(T("Только пока приложение открыто. До 30 отмеченных профилей за цикл. Журнал сервиса ограничен, возможны пропуски.", "Only while the app is open. Up to 30 tracked profiles per cycle. Provider logs are limited; gaps are possible.")), AsyncBtn(T("Обновить сейчас", "Refresh now"), Track))));
            page.Children.Add(Btn(T("Сохранить настройки", "Save settings"), () =>
            {
                options.Language = Value(language);
                options.NinjaEnabled = ninja.IsChecked == true;
                options.FindEnabled = find.IsChecked == true;
                options.OfficialEnabled = official.IsChecked == true;
                options.PreferredSource = Value(preferred);
                options.SetKey(key.Password);
                options.AutoRefresh = auto.IsChecked == true;
                options.IntervalMinutes = int.Parse(Value(interval));
                options.Save(Program.DataDir);
                watcher.Interval = TimeSpan.FromMinutes(options.IntervalMinutes);
                SetLanguage();
                BuildShell();
                Navigate("settings");
                SetStatus(T("Настройки сохранены.", "Settings saved."));
            }, true));
            var data = Vertical(Text(T("Локальная база и перенос", "Local library & transfer"), 18, null, true), Muted(Program.DataDir), Muted(T("Резервный архив не содержит API-ключ. Импортируются архивы ST4RR, профили API JSON и списки тегов TXT/CSV.", "Backups never contain the API key. Import ST4RR backups, API profile JSON, or TXT/CSV tag lists.")), Row(Btn(T("Резервная копия", "Back up library"), () => SaveJson(db.Export(), "ST4RR-backup.json")), AsyncBtn(T("Импорт", "Import"), Import), Btn(T("Открыть папку", "Open folder"), () => Process.Start(new ProcessStartInfo(Program.DataDir) { UseShellExecute = true }))));
            page.Children.Add(Card(data));
        }

        void AboutPage()
        {
            var brand = Branding.Lockup(true, true);
            var intro = Vertical(brand);
            page.Children.Add(Card(intro, 28));
            var providers = Vertical(Text(T("Источники", "Sources"), 18, null, true));
            var sourceLinks = new GapRow();
            providers.Children.Add(sourceLinks);
            foreach (var item in new[]
            {
                new[]
                {
                    "Brawl Time Ninja",
                    "https://brawltime.ninja"
                },
                new[]
                {
                    "BrawlFind",
                    "https://www.brawlfind.com"
                },
                new[]
                {
                    "Supercell API",
                    "https://developer.brawlstars.com"
                },
                new[]
                {
                    "Brawlify",
                    "https://brawlify.com"
                }
            }

            )
            {
                var url = item[1];
                var link = Btn(item[0] + "  ↗", () => Link(url));
                sourceLinks.Children.Add(link);
            }

            page.Children.Add(Card(providers));
            page.Children.Add(Card(Vertical(Text(T("Права", "Rights"), 18, null, true), Muted(T("ST4RR — неофициальное приложение, не связанное с Supercell и перечисленными сервисами. Brawl Stars и связанные материалы принадлежат Supercell. Названия и данные источников принадлежат их правообладателям.", "ST4RR is an unofficial app, unaffiliated with Supercell or the listed providers. Brawl Stars and related materials belong to Supercell. Provider names and data belong to their respective owners.")), Muted(T("Этот материал неофициальный и не одобрен Supercell. Подробнее: правила фанатского контента Supercell.", "This material is unofficial and is not endorsed by Supercell. For more information see Supercell's Fan Content Policy.")), Btn("Supercell Fan Content Policy ↗", () => Link("https://supercell.com/en/fan-content-policy/")), Muted("Newtonsoft.Json · MIT   /   LiteDB · MIT   /   Html Agility Pack · MIT", 11))));
        }

        void SaveJson(object value, string name)
        {
            var d = new SaveFileDialog
            {
                FileName = name,
                Filter = "JSON (*.json)|*.json"
            };
            if (d.ShowDialog(this) == true)
            {
                File.WriteAllText(d.FileName, JsonConvert.SerializeObject(value, Formatting.Indented), new System.Text.UTF8Encoding(false));
                SetStatus(T("Сохранено: ", "Saved: ") + d.FileName);
            }
        }

        void ExportProfile(Player p)
        {
            var archive = new Bundle
            {
                Players = new List<Player>
                {
                    p
                },
                Battles = db.ForPlayer(p.Tag),
                Snapshots = db.Snapshots.Find(s => s.Tag == p.Tag).ToList()
            };
            SaveJson(archive, "ST4RR-" + p.Tag.TrimStart('#') + ".json");
        }

        static string Csv(string s)
        {
            s = s ?? "";
            if (s.Length > 0 && "=+-@\t\r".Contains(s[0]))
                s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        void ExportResults()
        {
            var d = new SaveFileDialog
            {
                FileName = "ST4RR-results.csv",
                Filter = "CSV (*.csv)|*.csv|JSON (*.json)|*.json"
            };
            if (d.ShowDialog(this) != true)
                return;
            if (d.FilterIndex == 2)
            {
                File.WriteAllText(d.FileName, JsonConvert.SerializeObject(new Bundle { Players = visiblePlayers }, Formatting.Indented));
            }
            else
            {
                var lines = new List<string>
                {
                    "tag,name,trophies,club,creation_year,year_source,data_source,fetched_at_utc"
                };
                lines.AddRange(visiblePlayers.Select(p => string.Join(",", new[] { p.Tag, p.Name, p.Trophies?.ToString() ?? "", p.ClubName, p.Year?.ToString() ?? "", p.YearSource, p.Source, p.FetchedAt.ToString("O") }.Select(Csv))));
                File.WriteAllLines(d.FileName, lines, new System.Text.UTF8Encoding(true));
            }

            SetStatus(T("Результаты экспортированы.", "Results exported."));
        }

        async Task Import()
        {
            var d = new OpenFileDialog
            {
                Filter = "JSON / CSV / TXT|*.json;*.csv;*.txt"
            };
            if (d.ShowDialog(this) != true)
                return;
            await Run(async ct =>
            {
                if (new FileInfo(d.FileName).Length > 150000000)
                    throw new InvalidDataException(T("Максимальный размер файла — 150 МБ.", "Maximum file size is 150 MB."));
                string text = File.ReadAllText(d.FileName);
                if (System.IO.Path.GetExtension(d.FileName).ToLowerInvariant() == ".json")
                {
                    var j = JObject.Parse(text);
                    if (Util.Str(j["Application"]) == "ST4RR" || Util.Str(j["Application"]) == "ST4RRF1ND")
                    {
                        db.Import(j.ToObject<Bundle>());
                    }
                    else if (j["tag"] != null)
                    {
                        var p = Sources.ParsePlayer(j, "Import • " + System.IO.Path.GetFileName(d.FileName), "");
                        db.Save(p);
                        if (j["battles"] is JArray a)
                            db.AddBattles(Sources.ParseBattles(a, p.Tag, p.Source, true));
                    }
                    else
                        throw new InvalidDataException(T("Нужен архив ST4RR или JSON профиля API.", "Expected a ST4RR backup or API profile JSON."));
                    SetStatus(T("Архив импортирован.", "Archive imported."));
                }
                else
                {
                    var tags = Util.ReadTags(text, System.IO.Path.GetExtension(d.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase));
                    int count = 0;
                    foreach (var tag in tags.Take(1000))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!db.Players.Exists(p => p.Tag == tag))
                        {
                            db.Save(new Player { Tag = tag, Name = tag, Source = "Import • " + System.IO.Path.GetFileName(d.FileName) });
                            count++;
                        }
                    }

                    SetStatus(T("Добавлено тегов: ", "Tags added: ") + count + T(". Откройте профиль для загрузки данных.", ". Open a profile to fetch details."));
                }

                await Task.CompletedTask;
            });
        }

        void JsonDialog(string json, string source)
        {
            var d = new Window
            {
                Owner = this,
                Title = T("Исходные данные — ", "Raw data — ") + source,
                Width = 850,
                Height = 620,
                Background = Brush("#181A22"),
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var box = new TextBox
            {
                Text = json,
                IsReadOnly = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(16)
            };
            try
            {
                box.Text = JToken.Parse(json).ToString(Formatting.Indented);
            }
            catch
            {
            }

            d.Content = box;
            d.ShowDialog();
        }

        async Task Smoke()
        {
            var report = new List<string>();
            try
            {
                var fixture = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures", "ninja_profile.html");
                if (File.Exists(fixture))
                {
                    var r = Sources.ParseNinja(File.ReadAllText(fixture), "https://brawltime.ninja/profile/2PPQ");
                    db.Save(r.Player);
                    db.AddBattles(r.Battles);
                }

                var outdir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
                Directory.CreateDirectory(outdir);
                var lang = L.English ? "en" : "ru";
                var original = options.Language;
                Navigate("search");
                await Task.Delay(500);
                if (!loadingShown || !spinnerAnimated || !loadingDismissed || startupHost != null || startupHandle != new System.Windows.Interop.WindowInteropHelper(this).Handle)
                    throw new Exception("Same-window startup transition failed");
                report.Add("PASS full-size spinner and same-window startup transition");
                if (Descendants<DatePicker>(page).Any(x => x.IsDropDownOpen))
                    throw new Exception("Unexpected startup calendar");
                Shot(System.IO.Path.Combine(outdir, "search-" + lang + ".png"));
                CheckLayout();
                report.Add("PASS aligned fields and consistent button gaps");
                query.Text = "Sample Player";
                RefreshResults();
                if (visiblePlayers.Count != 1)
                    throw new Exception("UI local search failed");
                report.Add("PASS local text search");
                var p = db.Players.FindAll().FirstOrDefault(x => x.Detailed);
                if (p != null)
                {
                    var date = db.ForPlayer(p.Tag).First().Time.ToLocalTime().Date;
                    fromDate.SelectedDate = date;
                    toDate.SelectedDate = date;
                    RefreshResults();
                    if (!visiblePlayers.Any(x => x.Tag == p.Tag))
                        throw new Exception("UI inclusive date filter failed");
                    if (!string.Equals(fromDate.Language.IetfLanguageTag, L.English ? "en-US" : "ru-RU", StringComparison.OrdinalIgnoreCase))
                        throw new Exception("DatePicker language failed: " + fromDate.Language.IetfLanguageTag);
                    report.Add("PASS date filter and locale");
                    fromDate.ApplyTemplate();
                    var popup = fromDate.Template.FindName("PART_Popup", fromDate) as System.Windows.Controls.Primitives.Popup;
                    if (popup == null || popup.IsOpen || fromDate.IsDropDownOpen)
                        throw new Exception("Calendar opened without user action");
                    report.Add("PASS calendar remains closed on startup");
                    p.Favorite = true;
                    db.Players.Update(p);
                    Navigate("favorites");
                    if (!visiblePlayers.Any(x => x.Tag == p.Tag))
                        throw new Exception("UI favorites failed");
                    p.Favorite = false;
                    db.Players.Update(p);
                    report.Add("PASS favorites");
                    ProfilePage(p);
                    await Task.Delay(1300);
                    Shot(System.IO.Path.Combine(outdir, "profile-" + lang + ".png"));
                    compare.Clear();
                    ToggleCompare(p.Tag);
                    Navigate("compare");
                    await Task.Delay(1300);
                    Shot(System.IO.Path.Combine(outdir, "compare-" + lang + ".png"));
                    report.Add("PASS comparison");
                }

                foreach (var route in new[]
                {
                    "battles",
                    "clubs",
                    "rankings",
                    "events",
                    "settings",
                    "about"
                }

                )
                {
                    Navigate(route);
                    if (route == "about")
                    {
                        await Task.Delay(600);
                        var bootWord = Descendants<Grid>(page).First(x => x.Name == "BootWord");
                        var bootStar = Descendants<Grid>(page).First(x => x.Name == "BootStar");
                        if (bootWord.Opacity > .01 || bootStar.Opacity < .01)
                            throw new Exception("Boot sequence initial phase failed");
                        Shot(System.IO.Path.Combine(outdir, "about-intro-" + lang + ".png"));
                        await Task.Delay(1600);
                        if (bootWord.Opacity < .01)
                            throw new Exception("Boot reveal phase failed");
                        Shot(System.IO.Path.Combine(outdir, "about-reveal-" + lang + ".png"));
                        await Task.Delay(1800);
                        if (bootWord.Opacity < .99 || Descendants<TextBlock>(page).First(x => x.Name == "BootVersion").Opacity < .99)
                            throw new Exception("Boot sequence final phase failed");
                        report.Add("PASS four-second About boot sequence");
                    }
                    else
                        await Task.Delay(700);
                    Shot(System.IO.Path.Combine(outdir, route + "-" + lang + ".png"));
                    CheckLayout();
                    report.Add("PASS render and layout " + route);
                }

                Navigate("settings");
                UpdateLayout();
                var languages = Descendants<ComboBox>(page).First();
                if (languages.Items.Count != 2 || languages.Items.Cast<ComboBoxItem>().Any(x => (string)x.Tag == "auto"))
                    throw new Exception("Visible language choices failed");
                report.Add("PASS hidden automatic locale and RU/EN choices");
                if (Descendants<CheckBox>(page).Any(x => ((string)x.Content ?? "").Contains("Smooth") || ((string)x.Content ?? "").Contains("размыт")))
                    throw new Exception("Obsolete appearance controls");
                var sourceChecks = Descendants<CheckBox>(page).ToList();
                sourceChecks[0].IsChecked = false;
                sourceChecks[1].IsChecked = false;
                var saveSettings = Descendants<Button>(page).First(x => (x.Content as string) == T("Сохранить настройки", "Save settings"));
                saveSettings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (options.NinjaEnabled || options.FindEnabled)
                    throw new Exception("Provider controls failed");
                var saved = Options.Load(Program.DataDir);
                if (saved.FindEnabled || saved.NinjaEnabled)
                    throw new Exception("Provider settings not persisted");
                UpdateLayout();
                sourceChecks = Descendants<CheckBox>(page).ToList();
                sourceChecks[0].IsChecked = true;
                sourceChecks[1].IsChecked = true;
                Descendants<Button>(page).First(x => (x.Content as string) == T("Сохранить настройки", "Save settings")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                report.Add("PASS source selection and settings persistence");
                Navigate("about");
                await Task.Delay(750);
                if (Descendants<TextBlock>(page).Any(x => x.Text.Contains("ST4RRF1ND") || x.Text.Contains("Как читать данные")))
                    throw new Exception("About cleanup failed");
                report.Add("PASS renamed and simplified About page");
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    MinimizeAnimated();
                    await Task.Delay(210);
                    if (WindowState != WindowState.Minimized)
                        throw new Exception("Minimize failed");
                    WindowState = WindowState.Normal;
                    await Task.Delay(1400);
                    if (shell.Opacity < .99 || !shell.IsHitTestVisible)
                        throw new Exception("Restore animation failed: opacity=" + shell.Opacity + ", state=" + WindowState + ", hit=" + shell.IsHitTestVisible);
                }

                report.Add("PASS repeated minimize and restore");
                WindowState = WindowState.Maximized;
                await Task.Delay(1100);
                WindowState = WindowState.Normal;
                await Task.Delay(1400);
                if (shell.Opacity < .99)
                    throw new Exception("Maximize restore failed");
                report.Add("PASS maximize and restore");
                options.Language = "auto";
                SetLanguage();
                if (L.English != (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "ru"))
                    throw new Exception("Automatic language failed");
                report.Add("PASS initial Windows locale detection");
                options.Language = original == "en" ? "ru" : "en";
                SetLanguage();
                BuildShell();
                Navigate("search");
                if (header.Text != (L.English ? "Find players" : "Поиск игроков"))
                    throw new Exception("Language switch failed");
                report.Add("PASS runtime language switch");
                options.Language = original;
                SetLanguage();
                BuildShell();
                Navigate("search");
                Width = 1040;
                Height = 680;
                await Task.Delay(1100);
                Shot(System.IO.Path.Combine(outdir, "compact-" + lang + ".png"));
                report.Add("PASS compact window");
                File.WriteAllLines(System.IO.Path.Combine(outdir, "smoke-" + lang + ".txt"), report);
            }
            catch (Exception e)
            {
                File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smoke-failure.txt"), e.ToString());
            }

            Close();
        }

        void CheckLayout()
        {
            UpdateLayout();
            foreach (var row in Descendants<GapRow>(page))
            {
                var children = row.Children.Cast<FrameworkElement>().Where(x => x.IsVisible).ToList();
                for (int i = 1; i < children.Count; i++)
                {
                    var a = children[i - 1];
                    var b = children[i];
                    var ap = a.TranslatePoint(new Point(0, 0), row);
                    var bp = b.TranslatePoint(new Point(0, 0), row);
                    if (Math.Abs(ap.Y + a.ActualHeight - bp.Y - b.ActualHeight) < 1 && Math.Abs(bp.X - ap.X - a.ActualWidth - row.Gap) > 1.1)
                        throw new Exception("Uneven action spacing");
                }
            }

            foreach (var grid in Descendants<FormGrid>(page))
            {
                var children = grid.Children.Cast<FrameworkElement>().ToList();
                if (children.Count > 1 && children.Any(x => Math.Abs(x.ActualWidth - children[0].ActualWidth) > 1))
                    throw new Exception("Unequal form columns");
            }
        }

        static IEnumerable<T> Descendants<T>(DependencyObject root)
            where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    yield return match;
                foreach (var descendant in Descendants<T>(child))
                    yield return descendant;
            }
        }

        void Shot(string path)
        {
            UpdateLayout();
            var bmp = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(this);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(path))
                enc.Save(f);
        }
    }

    public static class ErrorEnglish
    {
        public static string Translate(string s)
        {
            var map = new Dictionary<string, string>
            {
                {
                    "Не удалось получить профиль.",
                    "Could not fetch the profile."
                },
                {
                    "Официальный API недоступен.",
                    "Official API is unavailable."
                },
                {
                    "Сохранённые данные остаются доступны.",
                    "Saved data remains available."
                },
                {
                    "Профиль не найден в публичном источнике.",
                    "Profile not found in the public provider."
                },
                {
                    "Источник не вернул корректный профиль.",
                    "Provider did not return a valid profile."
                },
                {
                    "Brawl Time Ninja изменил формат страницы или временно недоступен.",
                    "Brawl Time Ninja changed its page format or is temporarily unavailable."
                },
                {
                    "В публичной странице нет данных профиля.",
                    "The public page contains no profile data."
                },
                {
                    "Включите Brawl Time Ninja или настройте официальный API в разделе «Источники».",
                    "Enable Brawl Time Ninja or configure the official API in Settings."
                },
                {
                    "BrawlFind отключён в настройках источников. Доступен локальный поиск.",
                    "BrawlFind is disabled in Settings. Local search is available."
                },
                {
                    "Источник отклонил запрос; для API проверьте ключ и разрешённый IP.",
                    "Provider rejected the request; for API access, check the key and allowed IP."
                },
                {
                    "Запись не найдена.",
                    "Record not found."
                },
                {
                    "Попробуйте позднее.",
                    "Try again later."
                },
                {
                    "Источник временно ограничил запросы.",
                    "Provider temporarily rate-limited requests."
                },
                {
                    "Публичный источник не вернул ротацию событий.",
                    "Public provider did not return the event rotation."
                },
                {
                    "Источник не вернул состав клуба. Можно открыть его страницу или подключить официальный API.",
                    "Provider did not return a club roster. Open its website or connect the official API."
                },
                {
                    "Введите корректный тег клуба.",
                    "Enter a valid club tag."
                },
                {
                    "Тег содержит недопустимые символы.",
                    "Tag contains invalid characters."
                },
                {
                    "Некорректный тег",
                    "Invalid tag"
                },
                {
                    "Слишком большой ответ источника.",
                    "Provider response is too large."
                },
                {
                    "Это не архив ST4RR версии 1.",
                    "This is not a ST4RR version 1 backup."
                },
                {
                    "Слишком большой архив. Разделите его на части.",
                    "Backup is too large. Split it into smaller parts."
                },
                {
                    "В архиве некорректный тег или год.",
                    "Backup contains an invalid tag or year."
                },
                {
                    "Введите имя или начало тега (до 50 символов).",
                    "Enter a name or tag prefix (up to 50 characters)."
                }
            };
            foreach (var kv in map)
                s = s.Replace(kv.Key, kv.Value);
            if (System.Text.RegularExpressions.Regex.IsMatch(s, @"[А-Яа-я]"))
                return "The request failed. Check your connection and enabled sources. No saved data was removed.";
            return s;
        }
    }
}
