namespace GitConfigTool.Models
{
    /// <summary>
    /// 一个密钥对的文件路径与文本内容。
    /// </summary>
    public class SshKeyPairContent
    {
        /// <summary>密钥名（不含 .pub 后缀）</summary>
        public string Name { get; set; }

        /// <summary>私钥文件完整路径</summary>
        public string PrivatePath { get; set; }

        /// <summary>公钥文件完整路径</summary>
        public string PublicPath { get; set; }

        /// <summary>私钥路径（%USERPROFILE% 缩写，界面显示用）</summary>
        public string PrivatePathDisplay => Helpers.PathDisplay.Abbreviate(PrivatePath);

        /// <summary>公钥路径（%USERPROFILE% 缩写，界面显示用）</summary>
        public string PublicPathDisplay => Helpers.PathDisplay.Abbreviate(PublicPath);

        /// <summary>私钥文本</summary>
        public string PrivateText { get; set; } = string.Empty;

        /// <summary>公钥文本</summary>
        public string PublicText { get; set; } = string.Empty;

        /// <summary>私钥文件是否存在（不存在则不可编辑）</summary>
        public bool HasPrivateFile { get; set; }

        /// <summary>公钥文件是否存在（不存在则不可编辑）</summary>
        public bool HasPublicFile { get; set; }
    }
}
