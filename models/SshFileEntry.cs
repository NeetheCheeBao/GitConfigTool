using System;

namespace GitConfigTool.Models
{
    /// <summary>
    /// SSH 目录（%USERPROFILE%\.ssh）里的一个文件条目。
    /// </summary>
    public class SshFileEntry
    {
        /// <summary>文件名，如 id_ed25519 / id_ed25519.pub</summary>
        public string Name { get; set; }

        /// <summary>完整路径</summary>
        public string FullPath { get; set; }

        /// <summary>文件类型：私钥 / 公钥 / 配置 / 已知主机 / 授权公钥 / 签名白名单 / 其他</summary>
        public string Kind { get; set; }

        /// <summary>字节数</summary>
        public long Size { get; set; }

        /// <summary>最后修改时间</summary>
        public DateTime ModifiedAt { get; set; }

        /// <summary>界面显示用的大小</summary>
        public string SizeText => Size < 1024
            ? Size + " B"
            : (Size / 1024.0).ToString("0.0") + " KB";

        /// <summary>界面显示用的修改时间</summary>
        public string ModifiedText => ModifiedAt.ToString("yyyy-MM-dd HH:mm");
    }
}
