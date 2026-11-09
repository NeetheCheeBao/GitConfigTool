using System;
using System.Threading.Tasks;
using System.Windows;
using GitConfigTool.ViewModels;
using GitConfigTool.Views;

namespace GitConfigTool
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Helpers.Localization.Apply(Helpers.Localization.DefaultLanguageId);

            var launcher = new LauncherWindow();
            launcher.Loaded += async (s, args) => await StartAsync(launcher);
            launcher.Show();
        }

        private async Task StartAsync(LauncherWindow launcher)
        {
            IProgress<(string Text, int Percent)> progress =
                new Progress<(string Text, int Percent)>(p => launcher.SetProgress(p.Text, p.Percent));

            try
            {
                // 服务初始化会起外部进程做命令定位，放到后台线程，启动器的转圈才会一直流畅
                var vm = await Task.Run(() => new MainViewModel());
                await vm.InitializeAsync(progress);

                var main = new MainWindow(vm);
                MainWindow = main;
                main.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Helpers.Localization.T("msg.startup_failed") + ex.Message, Helpers.AppInfo.Name,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                launcher.Close();
            }
        }
    }
}
