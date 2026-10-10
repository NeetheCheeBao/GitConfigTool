using System.Windows;
using GitConfigTool.ViewModels;

namespace GitConfigTool.Views
{
    /// <summary>
    /// 完整配置独立窗口（编辑全局 git config 项）
    /// </summary>
    public partial class ConfigWindow : Window
    {
        public ConfigWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            // 复用主窗口的视图模型，因此配置项列表与选中状态和主窗口保持同步
            DataContext = viewModel;
        }
    }
}
