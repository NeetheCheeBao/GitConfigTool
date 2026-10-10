using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GitConfigTool.Models
{
    /// <summary>
    /// GitHub CLI 账号信息
    /// </summary>
    public class GithubAccount : INotifyPropertyChanged
    {
        private string _username = string.Empty;
        private string _name = string.Empty;
        private string _email = string.Empty;
        private string _host = "github.com";
        private bool _isActive;
        private string _tokenScope = string.Empty;
        private string _protocol = "https";

        public string Username
        {
            get => _username;
            set { _username = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Email
        {
            get => _email;
            set { _email = value; OnPropertyChanged(); }
        }

        public string Host
        {
            get => _host;
            set { _host = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 是否为当前生效（正在使用）的账号
        /// </summary>
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotActive)); }
        }

        /// <summary>
        /// IsActive 的反值，供界面按行控制按钮显隐使用
        /// </summary>
        public bool IsNotActive => !_isActive;

        public string TokenScope
        {
            get => _tokenScope;
            set { _tokenScope = value; OnPropertyChanged(); }
        }

        public string Protocol
        {
            get => _protocol;
            set { _protocol = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString() => $"{Username}@{Host}";
    }
}
