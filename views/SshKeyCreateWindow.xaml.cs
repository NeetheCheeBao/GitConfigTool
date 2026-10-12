using System;
using System.IO;
using System.Windows;
using GitConfigTool.Models;

namespace GitConfigTool.Views
{
    /// <summary>
    /// 创建 SSH 密钥对话框
    /// </summary>
    public partial class SshKeyCreateWindow : Window
    {
        /// <summary>不能用作密钥名的保留文件名</summary>
        private static readonly string[] ReservedNames =
        {
            "config", "known_hosts", "allowed_signers", "authorized_keys"
        };

        public SshKeyRequest Request { get; private set; }

        public SshKeyCreateWindow(SshKeyRequest request)
        {
            InitializeComponent();
            Request = request ?? new SshKeyRequest();
            DataContext = Request;
            PassphraseBox.Password = Request.Passphrase ?? string.Empty;
        }

        private void Passphrase_Changed(object sender, RoutedEventArgs e)
        {
            Request.Passphrase = PassphraseBox.Password;
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            var name = (Request.KeyName ?? string.Empty).Trim();

            if (name.Length == 0)
            {
                Warn("请填写密钥名");
                return;
            }

            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                Warn("密钥名不能包含 \\ / : * ? \" < > | 等字符");
                return;
            }

            foreach (var reserved in ReservedNames)
            {
                if (name.Equals(reserved, StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(reserved + ".pub", StringComparison.OrdinalIgnoreCase))
                {
                    Warn("「" + name + "」是 SSH 目录里的保留文件名，请换一个");
                    return;
                }
            }

            // 加入白名单需要标识，空标识会让这一步静默失效
            if (Request.AddToAllowedSigners && string.IsNullOrWhiteSpace(Request.Comment))
            {
                Warn("勾选了「自动将公钥加入白名单」，请先填写标识；\n" +
                     "白名单每行是「标识 算法 公钥」，标识通常是邮箱。\n\n" +
                     "如果不需要加入白名单，请取消勾选。");
                return;
            }

            Request.KeyName = name;
            Request.Passphrase = PassphraseBox.Password;
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
            MessageBox.Show(message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
