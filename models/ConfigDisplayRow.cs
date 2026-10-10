namespace GitConfigTool.Models
{
    /// <summary>
    /// 「当前全局配置」展示用的一行。
    /// 纯展示对象，不参与持久化。
    /// </summary>
    public class ConfigDisplayRow
    {
        /// <summary>对应的 git 配置键，例如 user.name（仅用于标识，界面不展示）</summary>
        public string ConfigKey { get; set; } = string.Empty;

        /// <summary>界面显示的中文名称，例如 提交者姓名</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>配置值</summary>
        public string Value { get; set; } = string.Empty;
    }
}
