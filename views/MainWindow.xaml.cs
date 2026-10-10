using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using GitConfigTool.ViewModels;

namespace GitConfigTool.Views
{
    /// <summary>
    /// 主窗口代码后台
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;

        /// <summary>日志行的颜色约定</summary>
        private static readonly Brush BrushCommand = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x6C));  // 黄色：执行的命令
        private static readonly Brush BrushResult = new SolidColorBrush(Color.FromRgb(0x6C, 0xD4, 0xE8));   // 青蓝：命令返回内容
        private static readonly Brush BrushError = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));    // 红色：出错信息
        private static readonly Brush BrushSuccess = new SolidColorBrush(Color.FromRgb(0x6B, 0xD4, 0x8A));  // 绿色：成功结果
        private static readonly Brush BrushInfo = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));     // 浅灰：普通信息

        /// <summary>把日志条目类型映射为对应颜色</summary>
        private static readonly System.Collections.Generic.Dictionary<Models.LogKind, Brush> KindBrushes =
            new System.Collections.Generic.Dictionary<Models.LogKind, Brush>
            {
                { Models.LogKind.Command, BrushCommand },
                { Models.LogKind.Result,  BrushResult },
                { Models.LogKind.Error,   BrushError },
                { Models.LogKind.Success, BrushSuccess },
                { Models.LogKind.Info,    BrushInfo },
            };

        /// <summary>从 "[HH:mm:ss] 正文" 中取出正文</summary>
        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            DataContext = _vm;

            _vm.RequestEditProfile += (s, e) =>
            {
                var dlg = new ProfileEditWindow(_vm.EditingProfile)
                {
                    Owner = this
                };
                if (dlg.ShowDialog() == true)
                {
                    _vm.EditingProfile = dlg.Profile;
                    if (_vm.SaveEditingProfileCommand.CanExecute(null))
                        _vm.SaveEditingProfileCommand.Execute(null);
                }
            };

            Loaded += async (s, e) => await _vm.InitializeAsync();

            // RichTextBox 不是依赖属性绑定目标，因此监听视图模型通知来重建内容
            _vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.CommandLog))
                    RenderLog();
            };
            Loaded += (s, e) => RenderLog();
        }

        /// <summary>
        /// 打开「完整配置」窗口（模态：打开期间主窗口锁定，禁止操作）
        /// </summary>
        private void OpenConfigWindow_Click(object sender, RoutedEventArgs e)
        {
            // 模态显示，阻塞到窗口关闭为止；因此不会再出现重复打开
            var dlg = new ConfigWindow(_vm) { Owner = this };
            dlg.ShowDialog();
        }

        /// <summary>
        /// 点击项目地址：用默认浏览器打开仓库页面
        /// </summary>
        private void OpenProjectUrl_Click(object sender, RoutedEventArgs e)
        {
            const string url = "https://github.com/NeetheCheeBao/GitConfigTool";
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("无法打开链接：" + ex.Message + "\n\n" + url,
                    "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 点击配置文件路径：在资源管理器中打开档案数据所在目录
        /// </summary>
        private void OpenProfilesFolder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _vm.OpenProfilesJsonFolderCommand.Execute(null);
        }

        /// <summary>
        /// 重建命令记录的彩色内容并滚动到最新一行。
        /// 用 RichTextBox 而不是 ItemsControl，是为了让文本可以被选中复制。
        /// 行前缀「执行:」不再单独显示，整行按类型着色。
        /// </summary>
        private void RenderLog()
        {
            if (LogDocument == null) return;

            LogDocument.Blocks.Clear();

            int count = 0;
            foreach (var entry in _vm.LogEntries)
            {
                var para = new Paragraph(new Run("[" + entry.TimeText + "] " + entry.Text))
                {
                    Margin = new Thickness(0, 0, 0, 2)
                };

                Brush brush;
                para.Foreground = KindBrushes.TryGetValue(entry.Kind, out brush) ? brush : BrushInfo;
                LogDocument.Blocks.Add(para);
                count++;
            }

            // 内容为空时给一个占位段落，避免高度塌陷
            if (count == 0)
                LogDocument.Blocks.Add(new Paragraph(new Run(string.Empty)) { Margin = new Thickness(0) });

            LogBox.ScrollToEnd();
        }
    }
}
