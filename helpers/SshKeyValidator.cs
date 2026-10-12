using System;
using System.Collections.Generic;
using System.Text;

namespace GitConfigTool.Helpers
{
    /// <summary>
    /// 校验 SSH 密钥文本的格式。全部在内存里完成，不调用外部命令，也不写临时文件。
    /// </summary>
    public static class SshKeyValidator
    {
        /// <summary>OpenSSH 私钥块解码后的魔数</summary>
        private static readonly byte[] OpenSshMagic = Encoding.ASCII.GetBytes("openssh-key-v1\0");

        /// <summary>已知的公钥算法名</summary>
        private static readonly string[] Algorithms =
        {
            "ssh-rsa", "ssh-dss", "ssh-ed25519",
            "ecdsa-sha2-nistp256", "ecdsa-sha2-nistp384", "ecdsa-sha2-nistp521",
            "sk-ssh-ed25519@openssh.com", "sk-ecdsa-sha2-nistp256@openssh.com"
        };

        /// <summary>
        /// 校验私钥文本：PEM 头尾配对、正文是合法 base64、OpenSSH 格式带正确魔数。
        /// </summary>
        public static (bool Ok, string Message) ValidatePrivateKey(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return (false, "内容为空");

            var lines = SplitLines(text);
            string label = null;
            int begin = -1, end = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();

                if (label == null &&
                    line.StartsWith("-----BEGIN ", StringComparison.Ordinal) &&
                    line.EndsWith("-----", StringComparison.Ordinal))
                {
                    label = line.Substring(11, line.Length - 16);
                    begin = i;
                }
                else if (label != null && line == "-----END " + label + "-----")
                {
                    end = i;
                    break;
                }
            }

            if (label == null) return (false, "找不到 -----BEGIN ...----- 起始行");
            if (end < 0) return (false, "找不到与起始行配对的 -----END ...----- 结束行");
            if (label.IndexOf("PRIVATE KEY", StringComparison.Ordinal) < 0)
                return (false, "这不是私钥块：" + label);

            var body = new StringBuilder();
            for (int i = begin + 1; i < end; i++) body.Append(lines[i].Trim());
            if (body.Length == 0) return (false, "私钥块正文为空");

            byte[] raw;
            try
            {
                raw = Convert.FromBase64String(body.ToString());
            }
            catch (FormatException)
            {
                return (false, "私钥块正文不是合法的 base64");
            }

            if (raw.Length == 0) return (false, "私钥块解码后为空");

            if (label == "OPENSSH PRIVATE KEY" && !StartsWith(raw, OpenSshMagic))
                return (false, "OpenSSH 私钥块缺少 openssh-key-v1 标识，内容可能已损坏");

            return (true, null);
        }

        /// <summary>
        /// 校验公钥文本：单行、算法名可识别、公钥是合法 base64，
        /// 且解码后的长度字段与算法名自洽。
        /// </summary>
        public static (bool Ok, string Message) ValidatePublicKey(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return (false, "内容为空");

            var trimmed = text.Trim();
            if (trimmed.IndexOf('\n') >= 0 || trimmed.IndexOf('\r') >= 0)
                return (false, "公钥应当只有一行");

            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return (false, "公钥至少需要「算法 公钥」两段");

            var algorithm = parts[0];
            if (Array.IndexOf(Algorithms, algorithm) < 0)
                return (false, "无法识别的算法名：" + algorithm);

            byte[] raw;
            try
            {
                raw = Convert.FromBase64String(parts[1]);
            }
            catch (FormatException)
            {
                return (false, "公钥不是合法的 base64");
            }

            // SSH 线格式：4 字节长度 + 算法名，之后才是密钥本体
            if (raw.Length < 4) return (false, "公钥内容过短");

            int nameLength = (raw[0] << 24) | (raw[1] << 16) | (raw[2] << 8) | raw[3];
            if (nameLength <= 0 || nameLength + 4 > raw.Length)
                return (false, "公钥的长度字段不合法");

            var name = Encoding.ASCII.GetString(raw, 4, nameLength);
            if (name != algorithm)
                return (false, "公钥内部的算法名（" + name + "）与首段（" + algorithm + "）不一致");

            return (true, null);
        }

        /// <summary>
        /// 校验白名单文本：每行至少三段（标识 算法 公钥），空行与 # 注释跳过。
        /// 末尾两段复用公钥校验。
        /// </summary>
        public static (bool Ok, string Message) ValidateAllowedSigners(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return (false, "内容为空");

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                    return (false, "第 " + (i + 1) + " 行少于三段（标识 算法 公钥）");

                var check = ValidatePublicKey(parts[parts.Length - 2] + " " + parts[parts.Length - 1]);
                if (!check.Ok)
                    return (false, "第 " + (i + 1) + " 行" + check.Message);
            }

            return (true, null);
        }

        private static List<string> SplitLines(string text)
        {
            var result = new List<string>();
            foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                result.Add(line);
            return result;
        }

        private static bool StartsWith(byte[] data, byte[] prefix)
        {
            if (data.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (data[i] != prefix[i]) return false;
            }
            return true;
        }
    }
}
