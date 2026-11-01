using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using DEX.App.ViewModels;

namespace DEX.App;

public partial class MainWindow : Window
{
    private static readonly TimeSpan IntroHold = TimeSpan.FromMilliseconds(2400);
    private static readonly TimeSpan IntroMaxDuration = TimeSpan.FromSeconds(10);
    private readonly ShellViewModel _model = new();
    private FrameworkElement? _page;
    private bool _introStarted;
    private bool _minHoldElapsed;
    private bool _introFinished;
    private bool _introTimedOut;
    private Brush? _windowBackground;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _model;
        _model.PropertyChanged += OnModelChanged;
        _page = PageHome;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        _introStarted = true;
        _windowBackground = Background;
        Background = Brushes.Black;
        var reduceMotion = !SystemParameters.ClientAreaAnimation;
        if (reduceMotion)
        {
            IntroLogo.Opacity = 1;
        }
        else
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1600))
            {
                EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
            };
            var scale = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(1600))
            {
                EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
            };
            IntroLogo.BeginAnimation(OpacityProperty, fade);
            IntroScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            IntroScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
            var blur = new BlurEffect { Radius = 20 };
            IntroLogo.Effect = blur;
            blur.BeginAnimation(BlurEffect.RadiusProperty,
                new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(1600))
                {
                    EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
                });
        }

        var hold = new DispatcherTimer { Interval = SystemParameters.ClientAreaAnimation ? IntroHold : TimeSpan.FromMilliseconds(600) };
        hold.Tick += (_, _) =>
        {
            hold.Stop();
            _minHoldElapsed = true;
            TryFinishIntro();
        };
        hold.Start();

        var failsafe = new DispatcherTimer { Interval = IntroMaxDuration };
        failsafe.Tick += (_, _) =>
        {
            failsafe.Stop();
            _introTimedOut = true;
            TryFinishIntro();
        };
        failsafe.Start();
    }

    private void TryFinishIntro()
    {
        if (_introFinished || !_introStarted || !_minHoldElapsed || !(_model.Ready || _introTimedOut))
            return;
        _introFinished = true;
        IntroOverlay.IsHitTestVisible = false;
        if (!SystemParameters.ClientAreaAnimation)
        {
            RemoveIntroOverlay();
            return;
        }
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(700))
        {
            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseInOut }
        };
        fadeOut.Completed += (_, _) => RemoveIntroOverlay();
        IntroOverlay.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void RemoveIntroOverlay()
    {
        IntroOverlay.BeginAnimation(OpacityProperty, null);
        IntroOverlay.Visibility = Visibility.Collapsed;
        if (_windowBackground is not null)
            Background = _windowBackground;
        if (IntroOverlay.Parent is Panel panel)
            panel.Children.Remove(IntroOverlay);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.Ready))
        {
            TryFinishIntro();
            return;
        }
        if (e.PropertyName is not (null or nameof(ShellViewModel.Page)))
            return;
        Dispatcher.InvokeAsync(() => ShowPage(_model.Page));
    }

    private void ShowPage(string page)
    {
        var next = page switch
        {
            "converter" => PageConverter,
            "history" => PageHistory,
            "formats" => PageFormats,
            "install" => PageInstall,
            "settings" => PageSettings,
            "about" => PageAbout,
            _ => PageHome
        };
        if (next == _page)
            return;
        var previous = _page;
        _page = next;
        SurfaceMotion.Enter(next);
        if (previous is null)
            return;
        SurfaceMotion.Exit(previous);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(230) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (previous != _page)
                SurfaceMotion.Hide(previous);
        };
        timer.Start();
    }

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            MaxClick(sender, e);
        else
            DragMove();
    }

    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaxClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            await _model.OpenPathAsync(files[0]);
    }
}