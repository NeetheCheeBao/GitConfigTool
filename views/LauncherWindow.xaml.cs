using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GitConfigTool.Views
{
    public partial class LauncherWindow : Window
    {
        private int _percent;
        private bool _busyMode;

        public LauncherWindow()
        {
            InitializeComponent();

            TitleText.Text = Helpers.AppInfo.Name + "   " + Helpers.AppInfo.VersionText;

            SpinnerRotate.BeginAnimation(
                RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                });

            ProgressTrack.SizeChanged += (s, e) => UpdateFill();
        }

        public void SetProgress(string text, int percent)
        {
            StatusText.Text = text;
            _percent = Math.Max(0, Math.Min(100, percent));
            PercentText.Text = _percent + "%";
            UpdateFill();
        }

        // 运行时忙碌复用同一套转圈，只留文字，不显示百分比与进度条
        public void UseBusyMode(string text)
        {
            _busyMode = true;
            TitleText.Text = Helpers.AppInfo.Name;
            StatusText.Text = text;
            PercentText.Visibility = Visibility.Collapsed;
            ProgressTrack.Visibility = Visibility.Collapsed;
            Height = 190;
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
        }

        public void SetBusyText(string text)
        {
            if (_busyMode) StatusText.Text = text;
        }

        private void UpdateFill()
        {
            ProgressFill.Width = ProgressTrack.ActualWidth * _percent / 100.0;
        }
    }
}
