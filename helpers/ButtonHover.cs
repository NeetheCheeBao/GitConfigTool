using System.Windows;
using System.Windows.Media;

namespace GitConfigTool.Helpers
{
    /// <summary>
    /// 给按钮挂上「悬停 / 按下」时的底色，让不同用途的按钮能有不同的淡色高亮。
    /// 按钮模板通过绑定读取这两个值，所以同一套模板可以服务所有按钮。
    /// </summary>
    public static class ButtonHover
    {
        /// <summary>悬停时的底色，默认浅灰</summary>
        public static readonly DependencyProperty HoverBackgroundProperty =
            DependencyProperty.RegisterAttached(
                "HoverBackground", typeof(Brush), typeof(ButtonHover),
                new PropertyMetadata(Frozen(0xE8, 0xE8, 0xE8)));

        public static Brush GetHoverBackground(DependencyObject element) =>
            (Brush)element.GetValue(HoverBackgroundProperty);

        public static void SetHoverBackground(DependencyObject element, Brush value) =>
            element.SetValue(HoverBackgroundProperty, value);

        /// <summary>按下时的底色，比悬停再深一档</summary>
        public static readonly DependencyProperty PressedBackgroundProperty =
            DependencyProperty.RegisterAttached(
                "PressedBackground", typeof(Brush), typeof(ButtonHover),
                new PropertyMetadata(Frozen(0xDC, 0xDC, 0xDC)));

        public static Brush GetPressedBackground(DependencyObject element) =>
            (Brush)element.GetValue(PressedBackgroundProperty);

        public static void SetPressedBackground(DependencyObject element, Brush value) =>
            element.SetValue(PressedBackgroundProperty, value);

        /// <summary>悬停时的边框色：与底色同色系但更饱和</summary>
        public static readonly DependencyProperty HoverBorderProperty =
            DependencyProperty.RegisterAttached(
                "HoverBorder", typeof(Brush), typeof(ButtonHover),
                new PropertyMetadata(Frozen(0x8C, 0x8C, 0x8C)));

        public static Brush GetHoverBorder(DependencyObject element) =>
            (Brush)element.GetValue(HoverBorderProperty);

        public static void SetHoverBorder(DependencyObject element, Brush value) =>
            element.SetValue(HoverBorderProperty, value);

        /// <summary>按下时的边框色，比悬停再深一档</summary>
        public static readonly DependencyProperty PressedBorderProperty =
            DependencyProperty.RegisterAttached(
                "PressedBorder", typeof(Brush), typeof(ButtonHover),
                new PropertyMetadata(Frozen(0x7A, 0x7A, 0x7A)));

        public static Brush GetPressedBorder(DependencyObject element) =>
            (Brush)element.GetValue(PressedBorderProperty);

        public static void SetPressedBorder(DependencyObject element, Brush value) =>
            element.SetValue(PressedBorderProperty, value);

        /// <summary>创建冻结的画刷：元数据里的默认值是全局共享的，必须冻结以免被意外修改</summary>
        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
