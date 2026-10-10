using System.Windows;
using GitConfigTool.Models;

namespace GitConfigTool.Views
{
    /// <summary>
    /// 档案编辑对话框
    /// </summary>
    public partial class ProfileEditWindow : Window
    {
        public GitProfile Profile { get; private set; }

        public ProfileEditWindow(GitProfile profile)
        {
            InitializeComponent();
            Profile = profile;
            DataContext = Profile;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Profile.UserName) ||
                string.IsNullOrWhiteSpace(Profile.Email))
            {
                MessageBox.Show("Git user.name 和 user.email 不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
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
