using System.Linq;
using System.Windows;
using GitConfigTool.Models;

namespace GitConfigTool.Views
{
    /// <summary>
    /// 白名单对账窗口：列出「公钥未加入白名单」和「白名单里本机找不到密钥」两类项，
    /// 由用户勾选后一次性应用。
    /// </summary>
    public partial class WhitelistReconcileWindow : Window
    {
        public WhitelistReconcileResult Result { get; private set; }

        public WhitelistReconcileWindow(WhitelistReconcileResult result)
        {
            InitializeComponent();

            Result = result;
            DataContext = Result;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (!Result.Items.Any(i => i.IsSelected && i.CanApply))
            {
                MessageBox.Show("没有勾选任何修改。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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
