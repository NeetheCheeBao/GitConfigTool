using System;

namespace GitConfigTool.Helpers
{
    /// <summary>
    /// 把绝对路径压缩成 Windows 环境变量写法，供界面显示。
    /// </summary>
    public static class PathDisplay
    {
        /// <summary>
        /// 用 %APPDATA% / %LOCALAPPDATA% / %USERPROFILE% 替换路径前缀，只影响显示。
        /// </summary>
        public static string Abbreviate(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // 先替换较长的前缀（%APPDATA% 位于 %USERPROFILE% 之下）
            path = ReplacePrefix(path, localAppData, "%LOCALAPPDATA%");
            path = ReplacePrefix(path, appData, "%APPDATA%");
            path = ReplacePrefix(path, userProfile, "%USERPROFILE%");
            return path;
        }

        private static string ReplacePrefix(string path, string prefix, string variable)
        {
            if (string.IsNullOrEmpty(prefix)) return path;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return path;

            // 必须落在目录分隔符上，避免 C:\Users\Ab 被 C:\Users\A 误替换
            if (path.Length > prefix.Length && path[prefix.Length] != '\\' && path[prefix.Length] != '/')
                return path;

            return variable + path.Substring(prefix.Length);
        }
    }
}
