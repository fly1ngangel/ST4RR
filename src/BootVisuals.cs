using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Starrfind
{
    public static class BootVisuals
    {
        static SolidColorBrush B(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color);
        static DoubleAnimation Tween(double from, double to, int duration, int delay = 0)
        {
            return new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(duration))
            {
                BeginTime = TimeSpan.FromMilliseconds(delay),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseInOut
                }
            };
        }

        public static FrameworkElement Spinner()
        {
            var ring = new Grid
            {
                Width = 38,
                Height = 38,
                Name = "StartupSpinner"
            };
            ring.Children.Add(new Ellipse { Stroke = B("#302B40"), StrokeThickness = 2.5 });
            var arc = new Path
            {
                Data = Geometry.Parse("M 19,1.5 A 17.5,17.5 0 0 1 36.5,19"),
                Stroke = B("#C8BAFF"),
                StrokeThickness = 2.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            var rotation = new RotateTransform();
            arc.RenderTransform = rotation;
            arc.RenderTransformOrigin = new Point(.5, .5);
            ring.Children.Add(arc);
            ring.Loaded += (s, e) => rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(950)) { RepeatBehavior = RepeatBehavior.Forever });
            ring.Unloaded += (s, e) => rotation.BeginAnimation(RotateTransform.AngleProperty, null);
            return ring;
        }

        public static FrameworkElement About()
        {
            var scene = new Canvas
            {
                Width = 440,
                Height = 224,
                HorizontalAlignment = HorizontalAlignment.Center,
                ClipToBounds = false,
                Name = "BootScene"
            };
            var starGroup = new Grid
            {
                Width = 90,
                Height = 90,
                Opacity = 0,
                Name = "BootStar"
            };
            Canvas.SetLeft(starGroup, 175);
            Canvas.SetTop(starGroup, 48);
            scene.Children.Add(starGroup);
            starGroup.Children.Add(Branding.Star(76));
            var transforms = new TransformGroup();
            var scale = new ScaleTransform(.5, .5);
            var rotate = new RotateTransform(-35);
            transforms.Children.Add(scale);
            transforms.Children.Add(rotate);
            starGroup.RenderTransform = transforms;
            starGroup.RenderTransformOrigin = new Point(.5, .5);
            var words = new Grid
            {
                Width = 277,
                Height = 105,
                Opacity = 0,
                Name = "BootWord"
            };
            Canvas.SetLeft(words, 131);
            Canvas.SetTop(words, 36);
            scene.Children.Add(words);
            var word = Branding.Word(70, "ST4RR", "#E9E4FF");
            words.Children.Add(word);
            var offset = new TranslateTransform();
            word.RenderTransform = offset;
            var version = new TextBlock
            {
                Text = (L.English ? "Version " : "Версия ") + Branding.Version,
                FontSize = 13,
                Foreground = B("#A2A6B8"),
                Opacity = 0,
                Width = 440,
                TextAlignment = TextAlignment.Center,
                Name = "BootVersion"
            };
            Canvas.SetTop(version, 176);
            scene.Children.Add(version);
            scene.Loaded += (s, e) =>
            {
                starGroup.BeginAnimation(UIElement.OpacityProperty, Tween(0, 1, 950));
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, Tween(.5, 1, 1200));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, Tween(.5, 1, 1200));
                rotate.BeginAnimation(RotateTransform.AngleProperty, Tween(-35, 0, 1300));
                starGroup.BeginAnimation(Canvas.LeftProperty, Tween(175, 18, 950, 1300));
                words.BeginAnimation(UIElement.OpacityProperty, Tween(0, 1, 650, 1800));
                offset.BeginAnimation(TranslateTransform.XProperty, Tween(-42, 0, 1200, 1800));
                version.BeginAnimation(UIElement.OpacityProperty, Tween(0, 1, 650, 3250));
            };
            return scene;
        }
    }
}
