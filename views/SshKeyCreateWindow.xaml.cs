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
                Warn(Helpers.Localization.T("rt.192"));
                return;
            }

            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                Warn(Helpers.Localization.T("rt.193"));
                return;
            }

            foreach (var reserved in ReservedNames)
            {
                if (name.Equals(reserved, StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(reserved + ".pub", StringComparison.OrdinalIgnoreCase))
                {
                    Warn("「" + name + Helpers.Localization.T("rt.194"));
                    return;
                }
            }

            // 加入白名单需要标识，空标识会让这一步静默失效
            if (Request.AddToAllowedSigners && string.IsNullOrWhiteSpace(Request.Comment))
            {
                Warn(Helpers.Localization.T("rt.195") +
                     Helpers.Localization.T("rt.196") +
                     Helpers.Localization.T("rt.197"));
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
            MessageBox.Show(message, Helpers.Localization.T("msg.title_hint"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
