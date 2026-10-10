using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GitConfigTool.Models
{
    /// <summary>
    /// Git 用户配置档案（账号）
    /// </summary>
    public class GitProfile : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString("N");
        private string _email = string.Empty;
        private string _userName = string.Empty;
        private bool _isActive;
        private string _signingKey = string.Empty;
        private string _signingFormat = "openpgp";
        private bool _commitGpgsign;
        private bool _tagGpgsign;
        private string _sshKeyPath = string.Empty;
        private string _githubUsername = string.Empty;
        private string _credentialHelper = string.Empty;
        private string _sslBackend = string.Empty;
        private DateTime _createdAt = DateTime.Now;
        private DateTime _lastUsedAt = DateTime.Now;

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Git user.name；同时作为档案在界面上的显示名
        /// </summary>
        public string UserName
        {
            get => _userName;
            set { _userName = value; OnPropertyChanged(); }
        }

        public string Email
        {
            get => _email;
            set { _email = value; OnPropertyChanged(); }
        }

        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); }
        }

        public string SigningKey
        {
            get => _signingKey;
            set { _signingKey = value; OnPropertyChanged(); }
        }

        public string SigningFormat
        {
            get => _signingFormat;
            set { _signingFormat = value; OnPropertyChanged(); }
        }

        public bool CommitGpgsign
        {
            get => _commitGpgsign;
            set { _commitGpgsign = value; OnPropertyChanged(); }
        }

        public bool TagGpgsign
        {
            get => _tagGpgsign;
            set { _tagGpgsign = value; OnPropertyChanged(); }
        }

        public string SshKeyPath
        {
            get => _sshKeyPath;
            set { _sshKeyPath = value; OnPropertyChanged(); }
        }

        public string GithubUsername
        {
            get => _githubUsername;
            set { _githubUsername = value; OnPropertyChanged(); }
        }

        public string CredentialHelper
        {
            get => _credentialHelper;
            set { _credentialHelper = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// HTTPS 后端（http.sslBackend）：openssl / schannel。
        /// 留空表示应用档案时不修改该配置项。
        /// </summary>
        public string SslBackend
        {
            get => _sslBackend;
            set { _sslBackend = value; OnPropertyChanged(); }
        }

        public DateTime CreatedAt
        {
            get => _createdAt;
            set { _createdAt = value; OnPropertyChanged(); }
        }

        public DateTime LastUsedAt
        {
            get => _lastUsedAt;
            set { _lastUsedAt = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString() => $"{UserName} <{Email}>";
    }
}
