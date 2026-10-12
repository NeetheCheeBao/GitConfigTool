using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GitConfigTool.Helpers;
using GitConfigTool.Models;

namespace GitConfigTool.Services
{
    /// <summary>
    /// 读取用户 SSH 目录（%USERPROFILE%\.ssh），只读，不修改任何文件。
    /// </summary>
    public class SshService
    {
        /// <summary>类型标记：私钥</summary>
        public const string KindPrivate = "私钥";

        /// <summary>类型标记：公钥</summary>
        public const string KindPublic = "公钥";

        /// <summary>类型标记：签名白名单</summary>
        public const string KindAllowedSigners = "签名白名单";

        /// <summary>白名单文件名</summary>
        private const string AllowedSignersFileName = "allowed_signers";

        /// <summary>已知的公钥算法前缀，用于识别没有 .pub 后缀的公钥文件</summary>
        private static readonly string[] PublicKeyPrefixes =
        {
            "ssh-rsa", "ssh-dss", "ssh-ed25519",
            "ecdsa-sha2-nistp256", "ecdsa-sha2-nistp384", "ecdsa-sha2-nistp521",
            "sk-ssh-ed25519@openssh.com", "sk-ecdsa-sha2-nistp256@openssh.com"
        };

        /// <summary>SSH 目录完整路径</summary>
        public string SshFolderPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

        /// <summary>SSH 目录是否存在</summary>
        public bool FolderExists => Directory.Exists(SshFolderPath);

        /// <summary>列出 SSH 目录下的全部文件，按文件名排序；目录不存在时返回空列表</summary>
        public List<SshFileEntry> ListFiles()
        {
            var result = new List<SshFileEntry>();
            if (!FolderExists) return result;

            string[] paths;
            try
            {
                paths = Directory.GetFiles(SshFolderPath);
            }
            catch (Exception)
            {
                // 目录存在但读不了（权限等），当作空目录处理
                return result;
            }

            foreach (var path in paths)
            {
                try
                {
                    var info = new FileInfo(path);
                    result.Add(new SshFileEntry
                    {
                        Name = info.Name,
                        FullPath = info.FullName,
                        Size = info.Length,
                        ModifiedAt = info.LastWriteTime,
                        Kind = Classify(info)
                    });
                }
                catch (Exception)
                {
                    // 单个文件读不到（被占用等）就跳过，不影响其余条目
                }
            }

            // 按文件名排序
            return result.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// 列出 SSH 目录的展示条目：签名白名单置顶，其后是各密钥（同名的私钥与公钥合并为一条）
        /// </summary>
        public List<SshListEntry> ListEntries()
        {
            var files = ListFiles();
            var result = new List<SshListEntry>();

            // 签名白名单置顶
            var whitelist = files.FirstOrDefault(f => f.Kind == KindAllowedSigners);
            if (whitelist != null)
            {
                result.Add(new SshListEntry
                {
                    Name = whitelist.Name,
                    IsWhitelist = true,
                    EntryCount = CountWhitelistEntries(whitelist.FullPath),
                    ModifiedAt = whitelist.ModifiedAt
                });
            }
            else if (FolderExists)
            {
                // 白名单文件不存在时占住这个位置，界面上渲染成「创建白名单文件」按钮
                result.Add(new SshListEntry
                {
                    Name = AllowedSignersFileName,
                    IsWhitelist = true,
                    IsWhitelistPlaceholder = true
                });
            }

            // 密钥：按「去掉 .pub 后缀」的名字分组
            var groups = files
                .Where(f => f.Kind == KindPrivate || f.Kind == KindPublic)
                .GroupBy(f => BaseName(f.Name), StringComparer.OrdinalIgnoreCase);

            result.AddRange(groups
                .Select(group =>
                {
                    var priv = group.FirstOrDefault(f => f.Kind == KindPrivate);
                    var pub = group.FirstOrDefault(f => f.Kind == KindPublic);

                    return new SshListEntry
                    {
                        Name = group.Key,
                        HasPrivateKey = priv != null,
                        HasPublicKey = pub != null,
                        PrivateKeySize = priv == null ? 0 : priv.Size,
                        PublicKeySize = pub == null ? 0 : pub.Size,
                        ModifiedAt = group.Max(f => f.ModifiedAt),
                        Algorithm = DetectAlgorithm(pub, priv)
                    };
                })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase));

            return result;
        }

        /// <summary>统计白名单里的有效条目数，跳过空行与 # 注释</summary>
        private static int CountWhitelistEntries(string path)
        {
            try
            {
                return File.ReadAllLines(path)
                    .Count(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"));
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// 取某个密钥对涉及的文件路径，只返回实际存在的
        /// </summary>
        public List<string> GetKeyPairFiles(string keyName)
        {
            var result = new List<string>();
            if (!FolderExists || string.IsNullOrWhiteSpace(keyName)) return result;

            var privatePath = Path.Combine(SshFolderPath, keyName);
            if (File.Exists(privatePath)) result.Add(privatePath);

            var publicPath = privatePath + ".pub";
            if (File.Exists(publicPath)) result.Add(publicPath);

            return result;
        }

        /// <summary>
        /// 调用 ssh-keygen 生成密钥对，同名文件已存在时拒绝
        /// </summary>
        public (bool Success, string Message) CreateKeyPair(SshKeyRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.KeyName))
                return (false, "密钥名不能为空");

            var sshKeygen = ExecutableLocator.LocateGitTool("ssh-keygen.exe");
            if (string.IsNullOrEmpty(sshKeygen))
                return (false, "未找到 ssh-keygen，请确认已安装 Git for Windows。");

            if (!Directory.Exists(SshFolderPath))
            {
                try
                {
                    Directory.CreateDirectory(SshFolderPath);
                }
                catch (Exception ex)
                {
                    return (false, "无法创建 SSH 目录：" + ex.Message);
                }
            }

            var privatePath = Path.Combine(SshFolderPath, request.KeyName);
            if (File.Exists(privatePath) || File.Exists(privatePath + ".pub"))
                return (false, "同名文件已存在，请换一个密钥名：" + request.KeyName);

            var arguments = "-t " + request.KeyType +
                            " -C \"" + request.Comment + "\"" +
                            " -f \"" + privatePath + "\"" +
                            " -N \"" + request.Passphrase + "\"" +
                            " -q";
            // ssh-keygen 的默认位数：rsa 是 3072、ecdsa 是 256，这里按界面上标注的规格指定
            if (string.Equals(request.KeyType, "rsa", StringComparison.OrdinalIgnoreCase))
                arguments = "-b 4096 " + arguments;
            else if (string.Equals(request.KeyType, "ecdsa", StringComparison.OrdinalIgnoreCase))
                arguments = "-b 521 " + arguments;

            var result = ExecutableLocator.Run(sshKeygen, arguments, null, 30000);
            if (result.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
                return (false, "ssh-keygen 执行失败：" +
                    (string.IsNullOrWhiteSpace(detail) ? "退出码 " + result.ExitCode : detail.Trim()));
            }

            if (!File.Exists(privatePath))
                return (false, "ssh-keygen 已执行，但没有找到生成的私钥文件");

            var message = "已创建密钥对 " + request.KeyName;

            if (request.AddToAllowedSigners && !string.IsNullOrWhiteSpace(request.Comment))
            {
                var added = AppendAllowedSigner(request.Comment.Trim(), privatePath + ".pub");
                message += added.Ok ? "，" + added.Message : "；白名单未写入：" + added.Message;
            }

            return (true, message);
        }

        /// <summary>
        /// 把公钥追加到白名单，格式为「标识 算法 公钥」；已存在同一行时不重复写入
        /// </summary>
        private (bool Ok, string Message) AppendAllowedSigner(string principal, string publicKeyPath)
        {
            try
            {
                if (!File.Exists(publicKeyPath)) return (false, "找不到公钥文件");

                var parts = File.ReadAllText(publicKeyPath)
                    .Trim()
                    .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return (false, "公钥内容无法解析");

                var line = principal + " " + parts[0] + " " + parts[1];
                var path = Path.Combine(SshFolderPath, AllowedSignersFileName);
                var existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
                var normalized = existing.Replace("\r\n", "\n").Replace('\r', '\n');

                foreach (var item in normalized.Split('\n'))
                {
                    if (item.Trim() == line) return (true, "白名单里已有该条目，未重复写入");
                }

                if (normalized.Length > 0 && !normalized.EndsWith("\n", StringComparison.Ordinal))
                    normalized += "\n";

                File.WriteAllText(path, normalized + line + "\n", new UTF8Encoding(false));
                return (true, "已加入白名单");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// 读取密钥对的私钥与公钥文本，供独立窗口显示；读不到时返回 null
        /// </summary>
        public SshKeyPairContent LoadKeyPairContent(string keyName)
        {
            if (!FolderExists || string.IsNullOrWhiteSpace(keyName)) return null;

            var privatePath = Path.Combine(SshFolderPath, keyName);
            var publicPath = privatePath + ".pub";

            var content = new SshKeyPairContent
            {
                Name = keyName,
                PrivatePath = privatePath,
                PublicPath = publicPath,
                HasPrivateFile = File.Exists(privatePath),
                HasPublicFile = File.Exists(publicPath)
            };

            try
            {
                if (content.HasPrivateFile) content.PrivateText = File.ReadAllText(privatePath);
                if (content.HasPublicFile) content.PublicText = File.ReadAllText(publicPath);
            }
            catch (Exception)
            {
                return null;
            }

            return content;
        }

        /// <summary>白名单文件完整路径</summary>
        public string AllowedSignersPath => Path.Combine(SshFolderPath, AllowedSignersFileName);

        /// <summary>
        /// 把本机密钥对与白名单双向比对：
        /// ① 本机有公钥但白名单里没有 → 建议加入
        /// ② 白名单里有但本机找不到对应密钥 → 可能是失效条目
        /// </summary>
        /// <param name="ownEmails">本机自己的 git 邮箱，用于判断白名单条目是否属于自己</param>
        public List<WhitelistReconcileItem> AnalyzeWhitelist(IEnumerable<string> ownEmails)
        {
            var items = new List<WhitelistReconcileItem>();
            if (!FolderExists) return items;

            var emails = new HashSet<string>(
                (ownEmails ?? Enumerable.Empty<string>())
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Select(e => e.Trim()),
                StringComparer.OrdinalIgnoreCase);

            // 本机所有公钥
            var localKeys = new List<(string Name, string Algo, string Blob, string Comment)>();
            foreach (var pub in ListFiles().Where(f => f.Kind == KindPublic))
            {
                var parts = ReadPublicKeyParts(pub.FullPath);
                if (parts == null) continue;
                localKeys.Add((BaseName(pub.Name), parts.Value.Algo, parts.Value.Blob, parts.Value.Comment));
            }

            var whitelistLines = ReadWhitelistLines();
            var whitelistBlobs = new HashSet<string>(whitelistLines.Select(l => l.Blob), StringComparer.Ordinal);
            var localBlobs = new HashSet<string>(localKeys.Select(k => k.Blob), StringComparer.Ordinal);

            // ① 公钥没进白名单
            foreach (var key in localKeys.Where(k => !whitelistBlobs.Contains(k.Blob)))
            {
                var hasPrincipal = !string.IsNullOrWhiteSpace(key.Comment);

                items.Add(new WhitelistReconcileItem
                {
                    Kind = WhitelistReconcileKind.Missing,
                    CanApply = hasPrincipal,
                    IsSelected = hasPrincipal,
                    Title = key.Name,
                    Detail = hasPrincipal
                        ? "标识 " + key.Comment + " · 公钥不在白名单里"
                        : "公钥没有注释，无法确定标识，需要手动处理",
                    Principal = key.Comment,
                    Algorithm = key.Algo,
                    PublicKeyBlob = key.Blob
                });
            }

            // ② 白名单里的条目本机找不到密钥
            foreach (var line in whitelistLines.Where(l => !localBlobs.Contains(l.Blob)))
            {
                // 标识是本机自己的邮箱才默认勾选删除；否则可能是同事的公钥
                var isOwn = emails.Contains(line.Principal);

                items.Add(new WhitelistReconcileItem
                {
                    Kind = WhitelistReconcileKind.Stale,
                    CanApply = true,
                    IsSelected = isOwn,
                    Title = line.Principal,
                    Detail = "本机找不到对应的密钥对",
                    Note = isOwn ? null : "标识不是本机邮箱，可能是同事的公钥，删除前请确认",
                    RawLine = line.Raw
                });
            }

            return items;
        }

        /// <summary>
        /// 按勾选项更新白名单：删除选中的失效行、追加选中的缺失公钥
        /// </summary>
        public (bool Ok, string Message) ApplyWhitelistChanges(IReadOnlyList<WhitelistReconcileItem> items)
        {
            var selected = (items ?? new List<WhitelistReconcileItem>())
                .Where(i => i.IsSelected && i.CanApply)
                .ToList();
            if (selected.Count == 0) return (false, "没有勾选任何修改");

            var path = AllowedSignersPath;
            if (!File.Exists(path)) return (false, "白名单文件不存在");

            try
            {
                var lines = File.ReadAllText(path)
                    .Replace("\r\n", "\n").Replace('\r', '\n')
                    .Split('\n')
                    .ToList();

                // 删除勾选的失效行（按原始整行精确匹配）
                var remove = new HashSet<string>(
                    selected.Where(i => i.Kind == WhitelistReconcileKind.Stale).Select(i => i.RawLine),
                    StringComparer.Ordinal);
                var kept = lines.Where(l => !remove.Contains(l)).ToList();

                // 去掉尾部空行，避免越积越多
                while (kept.Count > 0 && kept[kept.Count - 1].Trim().Length == 0)
                    kept.RemoveAt(kept.Count - 1);

                var added = 0;
                foreach (var item in selected.Where(i => i.Kind == WhitelistReconcileKind.Missing))
                {
                    var line = item.Principal.Trim() + " " + item.Algorithm + " " + item.PublicKeyBlob;
                    if (kept.Any(l => l.Trim() == line)) continue;
                    kept.Add(line);
                    added++;
                }

                var removed = lines.Count - kept.Count - added;
                File.WriteAllText(path, string.Join("\n", kept) + "\n", new UTF8Encoding(false));

                return (true, "已更新白名单：新增 " + added + " 条，删除 " + removed + " 条");
            }
            catch (Exception ex)
            {
                return (false, "写入失败：" + ex.Message);
            }
        }

        /// <summary>读取公钥文件的「算法 / base64 / 注释」三段</summary>
        private static (string Algo, string Blob, string Comment)? ReadPublicKeyParts(string path)
        {
            try
            {
                var parts = File.ReadAllText(path)
                    .Trim()
                    .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return null;

                var comment = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : string.Empty;
                return (parts[0], parts[1], comment);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>读取白名单里的有效行，跳过空行与 # 注释</summary>
        private List<(string Raw, string Principal, string Blob)> ReadWhitelistLines()
        {
            var result = new List<(string, string, string)>();
            var path = AllowedSignersPath;
            if (!File.Exists(path)) return result;

            try
            {
                foreach (var raw in File.ReadAllText(path)
                    .Replace("\r\n", "\n").Replace('\r', '\n')
                    .Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith("#", StringComparison.Ordinal)) continue;

                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 3) continue;

                    // 格式是「标识 [选项] 算法 公钥」，末尾两段固定是算法与公钥
                    result.Add((raw, parts[0], parts[parts.Length - 1]));
                }
            }
            catch (Exception)
            {
                // 读不到就当作没有条目，不影响其他功能
            }

            return result;
        }

        /// <summary>
        /// 取白名单文件路径，不存在时返回空列表
        /// </summary>
        public List<string> GetWhitelistFiles()
        {
            var path = AllowedSignersPath;
            return File.Exists(path) ? new List<string> { path } : new List<string>();
        }

        /// <summary>
        /// 新建一个空的白名单文件；已存在时拒绝覆盖
        /// </summary>
        public (bool Ok, string Message) CreateWhitelistFile()
        {
            var path = AllowedSignersPath;

            try
            {
                if (File.Exists(path)) return (false, "白名单文件已存在");
                if (!Directory.Exists(SshFolderPath)) Directory.CreateDirectory(SshFolderPath);

                File.WriteAllText(path, string.Empty, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                return (false, "创建失败：" + ex.Message);
            }

            return (true, "已创建白名单文件 " + AllowedSignersFileName);
        }

        /// <summary>
        /// 读取白名单文本，供独立窗口显示；文件不存在或读不到时返回 null
        /// </summary>
        public SshWhitelistContent LoadWhitelistContent()
        {
            var path = AllowedSignersPath;
            if (!File.Exists(path)) return null;

            try
            {
                return new SshWhitelistContent
                {
                    Path = path,
                    Text = File.ReadAllText(path)
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 校验并写回白名单
        /// </summary>
        public (bool Ok, string Message) SaveWhitelistContent(SshWhitelistContent content)
        {
            if (content == null) return (false, "没有可保存的内容");

            var check = SshKeyValidator.ValidateAllowedSigners(content.Text);
            if (!check.Ok) return (false, "白名单未保存：" + check.Message);

            try
            {
                WriteKeyFile(content.Path, content.Text);
            }
            catch (Exception ex)
            {
                return (false, "写入失败：" + ex.Message);
            }

            return (true, "已保存白名单");
        }

        /// <summary>
        /// 校验并写回密钥对中已存在的文件
        /// </summary>
        public (bool Ok, string Message) SaveKeyPairContent(SshKeyPairContent content)
        {
            if (content == null) return (false, "没有可保存的内容");
            if (!content.HasPrivateFile && !content.HasPublicFile)
                return (false, "没有找到可写入的文件");

            if (content.HasPrivateFile)
            {
                var check = SshKeyValidator.ValidatePrivateKey(content.PrivateText);
                if (!check.Ok) return (false, "私钥未保存：" + check.Message);
            }

            if (content.HasPublicFile)
            {
                var check = SshKeyValidator.ValidatePublicKey(content.PublicText);
                if (!check.Ok) return (false, "公钥未保存：" + check.Message);
            }

            try
            {
                if (content.HasPrivateFile) WriteKeyFile(content.PrivatePath, content.PrivateText);
                if (content.HasPublicFile) WriteKeyFile(content.PublicPath, content.PublicText);
            }
            catch (Exception ex)
            {
                return (false, "写入失败：" + ex.Message);
            }

            return (true, "已保存密钥「" + content.Name + "」");
        }

        /// <summary>
        /// 写密钥文件：换行统一成 LF（ssh-keygen 的格式，编辑框会带回 CRLF），结尾补一个换行
        /// </summary>
        private static void WriteKeyFile(string path, string text)
        {
            var normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            if (!normalized.EndsWith("\n", StringComparison.Ordinal)) normalized += "\n";

            File.WriteAllText(path, normalized, new UTF8Encoding(false));
        }

        /// <summary>去掉 .pub 后缀，得到密钥名</summary>
        private static string BaseName(string fileName) =>
            fileName.EndsWith(".pub", StringComparison.OrdinalIgnoreCase)
                ? fileName.Substring(0, fileName.Length - 4)
                : fileName;

        /// <summary>识别密钥算法，识别不出时返回「—」</summary>
        private static string DetectAlgorithm(SshFileEntry pub, SshFileEntry priv)
        {
            if (pub != null)
            {
                var firstLine = ReadFirstLine(pub.FullPath);
                if (!string.IsNullOrWhiteSpace(firstLine))
                {
                    var mapped = MapAlgorithm(firstLine.Trim().Split(' ')[0]);
                    if (mapped != null) return mapped;
                }
            }

            if (priv != null)
            {
                var header = ReadFirstLine(priv.FullPath);
                if (header.IndexOf("RSA PRIVATE KEY", StringComparison.OrdinalIgnoreCase) >= 0) return "RSA";
                if (header.IndexOf("EC PRIVATE KEY", StringComparison.OrdinalIgnoreCase) >= 0) return "ECDSA";
                if (header.IndexOf("DSA PRIVATE KEY", StringComparison.OrdinalIgnoreCase) >= 0) return "DSA";
            }

            return "—";
        }

        /// <summary>把公钥首行的算法标记映射成界面上的短名称</summary>
        private static string MapAlgorithm(string token)
        {
            switch (token.ToLowerInvariant())
            {
                case "ssh-ed25519": return "ED25519";
                case "sk-ssh-ed25519@openssh.com": return "ED25519-SK";
                case "ssh-rsa": return "RSA";
                case "ssh-dss": return "DSA";
                case "ecdsa-sha2-nistp256": return "ECDSA";
                case "ecdsa-sha2-nistp384": return "ECDSA";
                case "ecdsa-sha2-nistp521": return "ECDSA";
                case "sk-ecdsa-sha2-nistp256@openssh.com": return "ECDSA-SK";
                default: return null;
            }
        }

        /// <summary>按文件名与首行内容判断文件类型</summary>
        private static string Classify(FileInfo file)
        {
            var name = file.Name;

            if (name.Equals("config", StringComparison.OrdinalIgnoreCase)) return "配置";
            if (name.StartsWith("known_hosts", StringComparison.OrdinalIgnoreCase)) return "已知主机";
            if (name.Equals("authorized_keys", StringComparison.OrdinalIgnoreCase)) return "授权公钥";
            if (name.Equals(AllowedSignersFileName, StringComparison.OrdinalIgnoreCase)) return KindAllowedSigners;
            if (name.EndsWith(".pub", StringComparison.OrdinalIgnoreCase)) return KindPublic;

            var firstLine = ReadFirstLine(file.FullName);
            if (firstLine.IndexOf("PRIVATE KEY", StringComparison.OrdinalIgnoreCase) >= 0) return KindPrivate;
            if (IsPublicKeyLine(firstLine)) return KindPublic;

            return "其他";
        }

        /// <summary>读取文件第一行；失败时返回空串</summary>
        private static string ReadFirstLine(string path)
        {
            try
            {
                // 共享读，避免与正在使用该密钥的程序冲突
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadLine() ?? string.Empty;
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>首行是否以公钥算法开头</summary>
        private static bool IsPublicKeyLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            var firstToken = line.Trim().Split(' ')[0];
            return PublicKeyPrefixes.Any(p =>
                firstToken.Equals(p, StringComparison.OrdinalIgnoreCase));
        }
    }
}
