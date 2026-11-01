using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace DEX.App;

public static class SurfaceMotion
{
    private static readonly KeySpline Ease = new(0.16, 1, 0.3, 1);
    private static readonly DependencyProperty RestColorProperty =
        DependencyProperty.RegisterAttached("RestColor", typeof(Color), typeof(SurfaceMotion));

    public static readonly DependencyProperty HoverGlowProperty =
        DependencyProperty.RegisterAttached("HoverGlow", typeof(bool), typeof(SurfaceMotion), new PropertyMetadata(false, OnHoverGlow));

    public static void SetHoverGlow(DependencyObject element, bool value) => element.SetValue(HoverGlowProperty, value);
    public static bool GetHoverGlow(DependencyObject element) => (bool)element.GetValue(HoverGlowProperty);

    private static void OnHoverGlow(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true)
            return;

        element.Effect = new DropShadowEffect
        {
            Color = Color.FromRgb(0x11, 0x11, 0x13),
            BlurRadius = 22,
            ShadowDepth = 0,
            Opacity = 0
        };

        var rest = Colors.Transparent;
        if (element is Border border)
        {
            rest = border.BorderBrush is SolidColorBrush brush ? brush.Color : Color.FromArgb(0x14, 0x00, 0x00, 0x00);
            border.BorderBrush = new SolidColorBrush(rest);
            border.BorderThickness = border.BorderThickness == new Thickness(0) ? new Thickness(1) : border.BorderThickness;
        }

        element.SetValue(RestColorProperty, rest);
        element.MouseEnter += (_, _) => Glow(element, 0.10, Color.FromArgb(0x66, 0x11, 0x11, 0x13));
        element.MouseLeave += (_, _) => Glow(element, 0, (Color)element.GetValue(RestColorProperty));
    }

    private static void Glow(FrameworkElement element, double opacity, Color edge)
    {
        var fade = new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                (element.Effect as DropShadowEffect)?.BeginAnimation(DropShadowEffect.OpacityProperty, fade);

        if (element is Border { BorderBrush: SolidColorBrush brush })
        {
            var color = new ColorAnimation(edge, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, color);
        }
    }

    public static void Enter(FrameworkElement page)
    {
        page.Visibility = Visibility.Visible;
        page.Opacity = 0;
        Shift(page).Y = 8;
        Animate(page, 1, 0, 300);
    }

    public static void Exit(FrameworkElement page) => Animate(page, 0, -6, 220);

        public static void Hide(FrameworkElement page)
        {
            page.BeginAnimation(UIElement.OpacityProperty, null);
            page.Opacity = 1;
            var shift = Shift(page);
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = 0;
            page.Visibility = Visibility.Collapsed;
        }

    private static void Animate(FrameworkElement page, double opacity, double y, int milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var keyTime = KeyTime.FromTimeSpan(duration);
        var fade = new DoubleAnimationUsingKeyFrames { Duration = duration };
        fade.KeyFrames.Add(new SplineDoubleKeyFrame(opacity, keyTime, Ease));
        var slide = new DoubleAnimationUsingKeyFrames { Duration = duration };
        slide.KeyFrames.Add(new SplineDoubleKeyFrame(y, keyTime, Ease));
        page.BeginAnimation(UIElement.OpacityProperty, fade);
        Shift(page).BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private static TranslateTransform Shift(FrameworkElement page)
    {
        if (page.RenderTransform is TranslateTransform transform)
            return transform;
        transform = new TranslateTransform();
        page.RenderTransform = transform;
        return transform;
    }
}
