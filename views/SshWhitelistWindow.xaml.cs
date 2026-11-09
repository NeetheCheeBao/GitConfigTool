using System.Windows;
using System.Windows.Input;
using GitConfigTool.Helpers;
using GitConfigTool.Models;

namespace GitConfigTool.Views
{
    /// <summary>
    /// 白名单内容窗口：显示并编辑 allowed_signers
    /// </summary>
    public partial class SshWhitelistWindow : Window
    {
        /// <summary>窗口承载的白名单内容</summary>
        public SshWhitelistContent Whitelist { get; private set; }

        /// <summary>只读预览模式</summary>
        public bool ReadOnly { get; private set; }

        public SshWhitelistWindow(SshWhitelistContent content, bool readOnly)
        {
            InitializeComponent();

            Whitelist = content;
            ReadOnly = readOnly;
            DataContext = Whitelist;

            Title = readOnly ? Helpers.Localization.T("rt.204") : Helpers.Localization.T("rt.205");
            TitleText.Text = readOnly ? Helpers.Localization.T("rt.206") : Helpers.Localization.T("rt.205");
            ContentBox.IsReadOnly = readOnly;

            if (readOnly)
            {
                SaveButton.Visibility = Visibility.Collapsed;
                CancelButton.Content = Helpers.Localization.T("rt.201");
            }
        }

        /// <summary>左键点路径：在资源管理器中定位到文件</summary>
        private void Path_Click(object sender, MouseButtonEventArgs e)
        {
            ShellHelper.RevealInExplorer((sender as FrameworkElement)?.Tag as string);
        }

        /// <summary>右键点路径：复制到剪贴板</summary>
        private void Path_RightClick(object sender, MouseButtonEventArgs e)
        {
            var path = (sender as FrameworkElement)?.Tag as string;
            ShellHelper.CopyToClipboard(PathDisplay.Abbreviate(path));
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // 先在窗口内校验，不通过就不关闭，避免丢掉已编辑的内容
            var check = SshKeyValidator.ValidateAllowedSigners(Whitelist.Text);
            if (!check.Ok)
            {
                MessageBox.Show(Helpers.Localization.T("msg.whitelist_invalid") + check.Message, Helpers.Localization.T("msg.validation_failed"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
