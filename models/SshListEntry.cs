using System;
using System.Collections.Generic;

namespace GitConfigTool.Models
{
    /// <summary>
    /// SSH 目录列表里的一条展示条目：密钥，或签名白名单文件。
    /// </summary>
    public class SshListEntry
    {
        /// <summary>名称：密钥名（不含 .pub 后缀）或文件名</summary>
        public string Name { get; set; }

        /// <summary>算法，如 ED25519 / RSA / ECDSA；识别不出时为「—」</summary>
        public string Algorithm { get; set; }

        /// <summary>是否有私钥文件</summary>
        public bool HasPrivateKey { get; set; }

        /// <summary>是否有公钥文件</summary>
        public bool HasPublicKey { get; set; }

        /// <summary>私钥字节数</summary>
        public long PrivateKeySize { get; set; }

        /// <summary>公钥字节数</summary>
        public long PublicKeySize { get; set; }

        /// <summary>是否为签名白名单条目（列表置顶）</summary>
        public bool IsWhitelist { get; set; }

        /// <summary>
        /// 白名单文件还不存在时的占位条目：只在列表里占住白名单的位置，
        /// 界面上渲染成一个「创建白名单文件」按钮
        /// </summary>
        public bool IsWhitelistPlaceholder { get; set; }

        /// <summary>是否为真实文件条目（占位条目不是，不能双击、不能删除）</summary>
        public bool IsRealEntry => !IsWhitelistPlaceholder;

        /// <summary>是否为真实的白名单条目（只有它可以执行「识别密钥项目」）</summary>
        public bool CanReconcile => IsWhitelist && IsRealEntry;

        /// <summary>白名单里的条目数，仅白名单条目有意义</summary>
        public int EntryCount { get; set; }

        /// <summary>该条目相关文件的最后修改时间</summary>
        public DateTime ModifiedAt { get; set; }

        /// <summary>列表右侧标记：密钥对 / 仅私钥 / 仅公钥 / 白名单</summary>
        public string BadgeText =>
            IsWhitelist ? "白名单" :
            HasPrivateKey && HasPublicKey ? "密钥对" :
            HasPrivateKey ? "仅私钥" : "仅公钥";

        /// <summary>列表第二行：算法 · 私钥大小 · 公钥大小 · 修改时间</summary>
        public string SummaryText
        {
            get
            {
                var parts = new List<string>();
                if (IsWhitelist)
                {
                    parts.Add("签名白名单");
                    parts.Add(EntryCount + " 条记录");
                }
                else
                {
                    if (!string.IsNullOrEmpty(Algorithm)) parts.Add(Algorithm);
                    if (HasPrivateKey) parts.Add("私钥 " + SizeText(PrivateKeySize));
                    if (HasPublicKey) parts.Add("公钥 " + SizeText(PublicKeySize));
                }
                parts.Add(ModifiedAt.ToString("yyyy-MM-dd HH:mm"));
                return string.Join(" · ", parts);
            }
        }

        private static string SizeText(long size) => size < 1024
            ? size + " B"
            : (size / 1024.0).ToString("0.0") + " KB";
    }
}
