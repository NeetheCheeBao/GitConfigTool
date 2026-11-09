using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Controls;
using GitConfigTool.ViewModels;

namespace GitConfigTool.Views
{
    /// <summary>
    /// 主窗口代码后台
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private LauncherWindow _busyWindow;

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
        public MainWindow(MainViewModel vm)
        {
            InitializeComponent();

            // 标题栏版本号
            Title = Helpers.AppInfo.Title;

            _vm = vm;
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

            _vm.RequestCreateSshKey += (s, e) =>
            {
                var dlg = new SshKeyCreateWindow(_vm.KeyRequest)
                {
                    Owner = this
                };
                if (dlg.ShowDialog() == true)
                {
                    _vm.KeyRequest = dlg.Request;
                    if (_vm.CreateSshKeyCommand.CanExecute(null))
                        _vm.CreateSshKeyCommand.Execute(null);
                }
            };

            _vm.RequestOpenSshEntry += OpenSshEntry;

            _vm.RequestReconcileWhitelist += result =>
            {
                var dlg = new WhitelistReconcileWindow(result) { Owner = this };
                if (dlg.ShowDialog() == true)
                    _vm.ApplyWhitelistChanges(dlg.Result.Items);
            };

            // 目录监听要在窗口关闭时释放
            Closed += (s, e) =>
            {
                _busyWindow?.Close();
                _busyWindow = null;
                _vm.Dispose();
            };

            // 日志、忙碌小窗、状态栏都由视图模型通知驱动
            _vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.CommandLog))
                    RenderLog();
                else if (e.PropertyName == nameof(MainViewModel.IsBusy))
                    SyncBusyWindow();
                else if (e.PropertyName == nameof(MainViewModel.StatusMessage))
                    _busyWindow?.SetBusyText(_vm.StatusMessage);
            };
            Loaded += (s, e) => RenderLog();
        }

        // 忙碌小窗独占操作：禁用主窗口输入，避免加载期间误点其它命令
        private void SyncBusyWindow()
        {
            if (_vm.IsBusy)
            {
                if (_busyWindow != null) return;
                _busyWindow = new LauncherWindow();
                _busyWindow.UseBusyMode(_vm.StatusMessage);
                _busyWindow.Owner = this;
                _busyWindow.Show();
                IsEnabled = false;
            }
            else if (_busyWindow != null)
            {
                IsEnabled = true;
                _busyWindow.Close();
                _busyWindow = null;
            }
        }

        /// <summary>
        /// 点击项目地址：用默认浏览器打开仓库页面
        /// </summary>
        /// <summary>在默认浏览器中打开链接</summary>
        private void LanguageMenuButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            foreach (var lang in Helpers.Localization.Languages)
            {
                var item = new MenuItem
                {
                    Header = lang.Name,
                    IsCheckable = true,
                    IsChecked = lang.Id == _vm.SelectedLanguageId,
                    Tag = lang.Id
                };
                item.Click += LanguageMenuItem_Click;
                menu.Items.Add(item);
            }
            menu.PlacementTarget = (Button)sender;
            menu.IsOpen = true;
        }

        private void LanguageMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.SelectLanguage((string)((MenuItem)sender).Tag)) return;
            if (_vm.RefreshCommand.CanExecute(null))
                _vm.RefreshCommand.Execute(null);
        }

        private static void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(Helpers.Localization.T("msg.open_link_failed") + ex.Message + "\n\n" + url,
                    Helpers.Localization.T("msg.title_hint"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenProjectUrl_Click(object sender, RoutedEventArgs e) =>
            OpenUrl("https://github.com/NeetheCheeBao/GitConfigTool");

        private void OpenCreatorUrl_Click(object sender, RoutedEventArgs e) =>
            OpenUrl("https://github.com/NeetheCheeBao");

        private void CheckUpdate_Click(object sender, RoutedEventArgs e) =>
            OpenUrl("https://github.com/NeetheCheeBao/GitConfigTool/releases");

        /// <summary>
        /// 刷新按钮的视觉反馈：让列表快速淡出再淡回，闪一下表示确实刷新了
        /// </summary>
        private void FlashSshList_Click(object sender, RoutedEventArgs e)
        {
            var animation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1.0,
                To = 0.2,
                Duration = TimeSpan.FromMilliseconds(90),
                AutoReverse = true,
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            };

            SshEntryList.BeginAnimation(OpacityProperty, animation);
        }

        /// <summary>
        /// 行内「预览」「编辑」按钮：密钥开密钥窗口，白名单开白名单窗口
        /// </summary>
        private void OpenSshEntry(Models.SshListEntry entry, bool readOnly)
        {
            if (entry.IsWhitelist) OpenWhitelistWindow(entry, readOnly);
            else OpenKeyWindow(entry, readOnly);
        }

        private void OpenKeyWindow(Models.SshListEntry entry, bool readOnly)
        {
            var content = _vm.LoadKeyPairContent(entry);
            if (content == null)
            {
                MessageBox.Show(Helpers.Localization.T("msg.read_key_failed_prefix") + entry.Name + Helpers.Localization.T("msg.read_key_failed_suffix"),
                    Helpers.Localization.T("msg.title_hint"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new SshKeyViewWindow(content, readOnly) { Owner = this };

            // 必须先无条件显示对话框；只读模式下只是不保存而已
            var result = dlg.ShowDialog();
            if (!readOnly && result == true)
                _vm.SaveKeyPairContent(dlg.KeyPair);
        }

        private void OpenWhitelistWindow(Models.SshListEntry entry, bool readOnly)
        {
            var content = _vm.LoadWhitelistContent();
            if (content == null)
            {
                MessageBox.Show(Helpers.Localization.T("msg.read_whitelist_failed_prefix") + entry.Name + Helpers.Localization.T("msg.read_key_failed_suffix"),
                    Helpers.Localization.T("msg.title_hint"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new SshWhitelistWindow(content, readOnly) { Owner = this };

            // 必须先无条件显示对话框；只读模式下只是不保存而已
            var result = dlg.ShowDialog();
            if (!readOnly && result == true)
                _vm.SaveWhitelistContent(dlg.Whitelist);
        }

        /// <summary>
        /// 重建命令记录的彩色内容并滚动到最新一行，整行按类型着色
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
