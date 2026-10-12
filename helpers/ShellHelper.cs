using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace GitConfigTool.Helpers
{
    /// <summary>
    /// 与资源管理器、剪贴板交互的小工具。
    /// </summary>
    public static class ShellHelper
    {
        /// <summary>
        /// 在资源管理器中定位路径：文件会打开所在目录并选中它，目录则直接打开。
        /// </summary>
        public static bool RevealInExplorer(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                string argument;

                if (File.Exists(path))
                {
                    // explorer 的 /select 参数会打开所在目录并选中该文件
                    argument = "/select,\"" + path + "\"";
                }
                else
                {
                    // 文件还不存在（例如首次使用）：退而打开所在目录
                    var directory = Path.GetDirectoryName(path);
                    if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return false;
                    argument = "\"" + directory + "\"";
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = argument,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 把文本放进剪贴板，返回是否成功
        /// </summary>
        public static bool CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
