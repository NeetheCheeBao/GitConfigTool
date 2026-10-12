using System.Reflection;

namespace GitConfigTool.Helpers
{
    /// <summary>
    /// 应用名称与版本号，取自程序集属性（编译时由 csproj 从 version.ini 写入）。
    /// </summary>
    public static class AppInfo
    {
        /// <summary>应用名称，如 GitConfigTool</summary>
        public static string Name { get; } = ReadName();

        /// <summary>版本号，如 2.0.0</summary>
        public static string Version { get; } = ReadVersion();

        /// <summary>标题栏用：名称_版本</summary>
        public static string Title => Name + "_" + Version;

        /// <summary>「关于」页用：版本 2.0.0</summary>
        public static string VersionText => "版本 " + Version;

        private static string ReadName()
        {
            var attr = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyProductAttribute>();
            if (attr != null && !string.IsNullOrWhiteSpace(attr.Product)) return attr.Product.Trim();
            return "GitConfigTool";
        }

        private static string ReadVersion()
        {
            var attr = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (attr != null && !string.IsNullOrWhiteSpace(attr.InformationalVersion))
            {
                var v = attr.InformationalVersion.Trim();
                // 某些 SDK 会在版本号后追加 "+<提交哈希>"，这里只取前面的版本号
                var plus = v.IndexOf('+');
                if (plus >= 0) v = v.Substring(0, plus);
                if (v.Length > 0) return v;
            }
            return "2.0.0";
        }
    }
}
