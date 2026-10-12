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

            Title = readOnly ? "白名单预览" : "白名单内容";
            TitleText.Text = readOnly ? "白名单内容（只读）" : "白名单内容";
            ContentBox.IsReadOnly = readOnly;

            if (readOnly)
            {
                SaveButton.Visibility = Visibility.Collapsed;
                CancelButton.Content = "关闭";
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
                MessageBox.Show("白名单内容有误：" + check.Message, "内容校验未通过",
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
