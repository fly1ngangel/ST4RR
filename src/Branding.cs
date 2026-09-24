using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Starrfind
{
    public static class Branding
    {
        public const string Version = "1.0.0";
        public const string StarPath = "M 50,3 C 54,3 54,24 65,35 C 76,46 97,46 97,50 C 97,54 76,54 65,65 C 54,76 54,97 50,97 C 46,97 46,76 35,65 C 24,54 3,54 3,50 C 3,46 24,46 35,35 C 46,24 46,3 50,3 Z";
        static SolidColorBrush B(string c) => (SolidColorBrush)new BrushConverter().ConvertFromString(c);
        public static FrameworkElement Star(double size)
        {
            return new Path
            {
                Data = Geometry.Parse(StarPath),
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Fill = new LinearGradientBrush(Color.FromRgb(230, 224, 255), Color.FromRgb(151, 126, 238), 45)
            };
        }

        public static FrameworkElement Icon(string name, double size = 22)
        {
            string path;
            switch (name)
            {
                case "minimize":
                    path = "M3,12 L21,12";
                    break;
                case "maximize":
                    path = "M4,4 L20,4 L20,20 L4,20 Z";
                    break;
                case "close":
                    path = "M4,4 L20,20 M20,4 L4,20";
                    break;
                case "search":
                    path = "M16,16 L21,21 M18,10 A8,8 0 1 1 2,10 A8,8 0 1 1 18,10";
                    break;
                case "library":
                    path = "M4,3 L4,21 M9,3 L9,21 M14,4 L20,20 M2,21 L22,21";
                    break;
                case "favorites":
                    path = "M12,2 L15,9 L22,10 L17,15 L18,22 L12,18 L6,22 L7,15 L2,10 L9,9 Z";
                    break;
                case "battles":
                    path = "M12,6 L12,12 L16,14 M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12";
                    break;
                case "compare":
                    path = "M3,7 L21,7 L17,3 M21,17 L3,17 L7,21";
                    break;
                case "clubs":
                    path = "M12,2 L21,6 L20,15 Q18,20 12,23 Q6,20 4,15 L3,6 Z M8,11 L11,14 L16,9";
                    break;
                case "rankings":
                    path = "M3,20 L3,15 L8,15 L8,20 M10,20 L10,9 L15,9 L15,20 M17,20 L17,3 L22,3 L22,20";
                    break;
                case "events":
                    path = "M4,5 L20,5 L20,22 L4,22 Z M8,2 L8,8 M16,2 L16,8 M4,11 L20,11 M8,15 L11,15 M14,18 L17,18";
                    break;
                case "settings":
                    path = "M3,6 L21,6 M3,18 L21,18 M8,3 L8,9 M16,15 L16,21";
                    break;
                default:
                    path = "M12,10 L12,18 M12,6 L12,6.2 M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12";
                    break;
            }

            return new Path
            {
                Data = Geometry.Parse(path),
                Stroke = B("#C8C2DD"),
                StrokeThickness = 1.7,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                IsHitTestVisible = false
            };
        }

        internal static TextBlock Word(double size, string text, string color)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = size,
                FontWeight = FontWeights.Bold,
                FontStyle = FontStyles.Italic,
                Foreground = B(color)
            };
        }

        public static FrameworkElement Lockup(bool large = false, bool animate = false)
        {
            if (large && animate)
                return BootVisuals.About();
            double size = large ? 66 : 28;
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var star = Star(large ? 72 : 32);
            star.VerticalAlignment = VerticalAlignment.Center;
            star.Margin = new Thickness(0, 0, large ? 24 : 12, 0);
            panel.Children.Add(star);
            var words = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            panel.Children.Add(words);
            var word = Word(size, "ST4RR", "#E9E4FF");
            words.Children.Add(word);
            return panel;
        }
    }
}
