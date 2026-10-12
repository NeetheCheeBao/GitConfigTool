using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GitConfigTool.Models
{
    /// <summary>
    /// 创建 SSH 密钥对时的输入参数。
    /// </summary>
    public class SshKeyRequest : INotifyPropertyChanged
    {
        private string _keyName = string.Empty;
        private string _keyType = "ed25519";
        private string _comment = string.Empty;
        private string _passphrase = string.Empty;
        private bool _addToAllowedSigners = true;

        /// <summary>密钥名，对应 .ssh 下的文件名（不含 .pub 后缀）</summary>
        public string KeyName
        {
            get => _keyName;
            set { _keyName = value; OnPropertyChanged(); }
        }

        /// <summary>算法：ed25519 / rsa / ecdsa</summary>
        public string KeyType
        {
            get => _keyType;
            set { _keyType = value; OnPropertyChanged(); }
        }

        /// <summary>注释，通常填邮箱；加入白名单时也作为标识</summary>
        public string Comment
        {
            get => _comment;
            set
            {
                _comment = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanAddToAllowedSigners));
            }
        }

        /// <summary>密码，留空表示不设密码</summary>
        public string Passphrase
        {
            get => _passphrase;
            set { _passphrase = value; OnPropertyChanged(); }
        }

        /// <summary>是否把新公钥加入白名单（allowed_signers）</summary>
        public bool AddToAllowedSigners
        {
            get => _addToAllowedSigners;
            set { _addToAllowedSigners = value; OnPropertyChanged(); }
        }

        /// <summary>注释非空才可作为白名单标识</summary>
        public bool CanAddToAllowedSigners => !string.IsNullOrWhiteSpace(_comment);

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
