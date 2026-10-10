using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GitConfigTool.Models
{
    /// <summary>
    /// Git 配置项（键值对）
    /// </summary>
    public class GitConfigItem : INotifyPropertyChanged
    {
        private string _section = string.Empty;
        private string _key = string.Empty;
        private string _value = string.Empty;
        private string _scope = "global";
        private bool _isModified;

        public string Section
        {
            get => _section;
            set { _section = value; OnPropertyChanged(); OnPropertyChanged(nameof(FullKey)); }
        }

        public string Key
        {
            get => _key;
            set { _key = value; OnPropertyChanged(); OnPropertyChanged(nameof(FullKey)); }
        }

        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); IsModified = true; }
        }

        public string Scope
        {
            get => _scope;
            set { _scope = value; OnPropertyChanged(); }
        }

        public bool IsModified
        {
            get => _isModified;
            set { _isModified = value; OnPropertyChanged(); }
        }

        public string FullKey => string.IsNullOrEmpty(Section) ? Key : $"{Section}.{Key}";

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
