using System;
using System.Windows;
using System.Windows.Controls;

namespace Starrfind
{
    // Gaps belong to the container, so the first/last item never gets a stray margin.
    public class SpacedStack : StackPanel
    {
        public double Gap { get; set; } = 12;

        protected override Size MeasureOverride(Size available)
        {
            double width = 0, height = 0;
            int count = 0;
            foreach (UIElement child in InternalChildren)
            {
                if (child.Visibility == Visibility.Collapsed)
                    continue;
                child.Measure(new Size(available.Width, double.PositiveInfinity));
                width = Math.Max(width, child.DesiredSize.Width);
                height += child.DesiredSize.Height;
                if (count++ > 0)
                    height += Gap;
            }

            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double y = 0;
            int count = 0;
            foreach (UIElement child in InternalChildren)
            {
                if (child.Visibility == Visibility.Collapsed)
                    continue;
                if (count++ > 0)
                    y += Gap;
                child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height));
                y += child.DesiredSize.Height;
            }

            return finalSize;
        }
    }

    public class GapRow : Panel
    {
        public double Gap { get; set; } = 12;

        protected override Size MeasureOverride(Size available)
        {
            double x = 0, line = 0, total = 0, width = 0;
            foreach (UIElement child in InternalChildren)
            {
                if (child.Visibility == Visibility.Collapsed)
                    continue;
                child.Measure(available);
                double w = child.DesiredSize.Width;
                if (x > 0 && x + Gap + w > available.Width)
                {
                    width = Math.Max(width, x);
                    total += line + Gap;
                    x = 0;
                    line = 0;
                }

                if (x > 0)
                    x += Gap;
                x += w;
                line = Math.Max(line, child.DesiredSize.Height);
            }

            return new Size(Math.Max(width, x), total + line);
        }

        protected override Size ArrangeOverride(Size size)
        {
            int start = 0;
            double y = 0;
            while (start < InternalChildren.Count)
            {
                int end = start;
                double width = 0, height = 0;
                while (end < InternalChildren.Count)
                {
                    var c = InternalChildren[end];
                    double next = width + (end > start ? Gap : 0) + c.DesiredSize.Width;
                    if (end > start && next > size.Width)
                        break;
                    width = next;
                    height = Math.Max(height, c.DesiredSize.Height);
                    end++;
                }

                double x = 0;
                for (int i = start; i < end; i++)
                {
                    var c = InternalChildren[i];
                    c.Arrange(new Rect(x, y + height - c.DesiredSize.Height, c.DesiredSize.Width, c.DesiredSize.Height));
                    x += c.DesiredSize.Width + Gap;
                }

                y += height + Gap;
                start = end;
            }

            return size;
        }
    }

    public class FormGrid : Panel
    {
        public double Gap { get; set; } = 16;
        public int MaxColumns { get; set; } = 4;

        int Columns(double width) => width >= MaxColumns * 150 + (MaxColumns - 1) * Gap ? MaxColumns : Math.Min(2, MaxColumns);
        protected override Size MeasureOverride(Size available)
        {
            int cols = Columns(available.Width);
            double width = (available.Width - Gap * (cols - 1)) / cols, total = 0, row = 0;
            for (int i = 0; i < InternalChildren.Count; i++)
            {
                var child = InternalChildren[i];
                child.Measure(new Size(width, double.PositiveInfinity));
                row = Math.Max(row, child.DesiredSize.Height);
                if (i % cols == cols - 1 || i == InternalChildren.Count - 1)
                {
                    total += row + (i >= cols ? Gap : 0);
                    row = 0;
                }
            }

            return new Size(available.Width, total);
        }

        protected override Size ArrangeOverride(Size size)
        {
            int cols = Columns(size.Width);
            double width = (size.Width - Gap * (cols - 1)) / cols, y = 0;
            for (int start = 0; start < InternalChildren.Count; start += cols)
            {
                int end = Math.Min(start + cols, InternalChildren.Count);
                double height = 0;
                for (int i = start; i < end; i++)
                    height = Math.Max(height, InternalChildren[i].DesiredSize.Height);
                for (int i = start; i < end; i++)
                    InternalChildren[i].Arrange(new Rect((i - start) * (width + Gap), y, width, height));
                y += height + Gap;
            }

            return size;
        }
    }
}
