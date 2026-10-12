namespace GitConfigTool.Models
{
    /// <summary>
    /// 白名单（allowed_signers）的文件路径与文本内容。
    /// </summary>
    public class SshWhitelistContent
    {
        /// <summary>白名单文件完整路径</summary>
        public string Path { get; set; }

        /// <summary>路径（%USERPROFILE% 缩写，界面显示用）</summary>
        public string PathDisplay => Helpers.PathDisplay.Abbreviate(Path);

        /// <summary>白名单文本</summary>
        public string Text { get; set; } = string.Empty;
    }
}
