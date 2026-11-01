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
        MouseMove += OnAuraMouseMove;
        CompositionTarget.Rendering += OnAuraFrame;
    }

    private double _auraTargetX, _auraTargetY, _auraX, _auraY;

    private void OnAuraMouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        _auraTargetX = (p.X / Math.Max(1, ActualWidth) - 0.5) * 2;
        _auraTargetY = (p.Y / Math.Max(1, ActualHeight) - 0.5) * 2;
    }

    private void OnAuraFrame(object? sender, EventArgs e)
    {
        if (!IsLoaded || !SystemParameters.ClientAreaAnimation)
            return;
        _auraX += (_auraTargetX - _auraX) * 0.06;
        _auraY += (_auraTargetY - _auraY) * 0.06;
        AuraWarmMX.X = _auraX * 120;
        AuraWarmMY.Y = _auraY * 90;
        AuraPlumMX.X = -_auraX * 160;
        AuraPlumMY.Y = -_auraY * 110;
    }

    private void StartAuraAnimations()
    {
        var sb = new Storyboard();
        void Add(string target, string prop, double from, double to, double seconds, double beginSeconds, bool reverse)
        {
            var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
            {
                BeginTime = TimeSpan.FromSeconds(beginSeconds),
                AutoReverse = reverse,
                RepeatBehavior = RepeatBehavior.Forever,
            };
            if (reverse)
                anim.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            Storyboard.SetTargetName(anim, target);
            Storyboard.SetTargetProperty(anim, new PropertyPath(prop));
            sb.Children.Add(anim);
        }

        Add("AuraWarmX", "X", -160, 180, 60, 0, true);
        Add("AuraWarmY", "Y", -80, 100, 70, 0, true);
        Add("AuraPlumX", "X", 140, -200, 72, 0, true);
        Add("AuraPlumY", "Y", 70, -90, 80, 0, true);
        Add("AuraWarmS", "ScaleX", 0.94, 1.06, 26, 0, true);
        Add("AuraWarmS", "ScaleY", 0.94, 1.06, 26, 0, true);
        Add("AuraPlumS", "ScaleX", 1.05, 0.95, 34, 0, true);
        Add("AuraPlumS", "ScaleY", 1.05, 0.95, 34, 0, true);

        sb.Begin(this, true);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        StartAuraAnimations();
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
            "account" => PageAccount,
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