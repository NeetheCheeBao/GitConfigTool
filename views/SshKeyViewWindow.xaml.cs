using System.Windows;
using System.Windows.Input;
using GitConfigTool.Helpers;
using GitConfigTool.Models;

namespace GitConfigTool.Views
{
    /// <summary>
    /// 密钥内容窗口：左右两个框分别显示私钥与公钥，均可编辑
    /// </summary>
    public partial class SshKeyViewWindow : Window
    {
        /// <summary>窗口承载的密钥对内容（名字避开 WPF 的 Window.Content）</summary>
        public SshKeyPairContent KeyPair { get; private set; }

        /// <summary>只读预览模式</summary>
        public bool ReadOnly { get; private set; }

        public SshKeyViewWindow(SshKeyPairContent content, bool readOnly)
        {
            InitializeComponent();

            KeyPair = content;
            ReadOnly = readOnly;
            DataContext = KeyPair;

            Title = (readOnly ? "密钥预览 - " : "密钥内容 - ") + content.Name;
            TitleText.Text = readOnly ? content.Name + "（只读）" : content.Name;

            // 文件不存在的框不可编辑；预览模式下两个框都只读
            PrivateBox.IsReadOnly = readOnly || !content.HasPrivateFile;
            PublicBox.IsReadOnly = readOnly || !content.HasPublicFile;

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
            if (KeyPair.HasPrivateFile)
            {
                var check = SshKeyValidator.ValidatePrivateKey(KeyPair.PrivateText);
                if (!check.Ok)
                {
                    Warn("私钥内容有误：" + check.Message);
                    return;
                }
            }

            if (KeyPair.HasPublicFile)
            {
                var check = SshKeyValidator.ValidatePublicKey(KeyPair.PublicText);
                if (!check.Ok)
                {
                    Warn("公钥内容有误：" + check.Message);
                    return;
                }
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static void Warn(string message)
        {
            MessageBox.Show(message, "内容校验未通过", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
