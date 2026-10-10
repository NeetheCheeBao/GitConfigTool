using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GitConfigTool.Models;

namespace GitConfigTool.Services
{
    /// <summary>
    /// 本地档案持久化服务（简易 JSON，无第三方依赖）
    /// </summary>
    public class ProfileStorageService
    {
        /// <summary>数据文件名</summary>
        private const string DataFileName = "GitConfig.json";

        /// <summary>旧版数据文件名（用于自动迁移）</summary>
        private const string LegacyDataFileName = "profiles.json";

        private readonly string _storagePath;

        /// <summary>本次启动是否执行了数据文件迁移</summary>
        public string MigrationNote { get; private set; }

        public ProfileStorageService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "GitConfigTool");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _storagePath = Path.Combine(dir, DataFileName);
            MigrateLegacyFile(dir);
        }

        /// <summary>
        /// 把旧版 profiles.json 迁移为 GitConfig.json。
        /// 仅在旧文件存在且新文件不存在时执行，避免覆盖已有数据。
        /// </summary>
        private void MigrateLegacyFile(string dir)
        {
            try
            {
                var legacy = Path.Combine(dir, LegacyDataFileName);
                if (!File.Exists(legacy)) return;
                if (File.Exists(_storagePath)) return;   // 新文件已存在，不动

                File.Move(legacy, _storagePath);
                MigrationNote = "已把旧数据文件 " + LegacyDataFileName + " 重命名为 " + DataFileName;
            }
            catch (Exception ex)
            {
                MigrationNote = "数据文件迁移失败：" + ex.Message;
            }
        }

        public string StoragePath => _storagePath;

        public Task<List<GitProfile>> LoadProfilesAsync()
        {
            return Task.Run(() =>
            {
                if (!File.Exists(_storagePath))
                    return new List<GitProfile>();

                try
                {
                    var json = File.ReadAllText(_storagePath, Encoding.UTF8);
                    return ParseProfiles(json);
                }
                catch
                {
                    return new List<GitProfile>();
                }
            });
        }

        public Task SaveProfilesAsync(IEnumerable<GitProfile> profiles)
        {
            return Task.Run(() =>
            {
                var json = SerializeProfiles(profiles.ToList());
                File.WriteAllText(_storagePath, json, Encoding.UTF8);
            });
        }

        public async Task UpsertProfileAsync(GitProfile profile)
        {
            var list = await LoadProfilesAsync();
            var existing = list.FirstOrDefault(p => p.Id == profile.Id);
            if (existing != null)
            {
                var idx = list.IndexOf(existing);
                list[idx] = profile;
            }
            else
            {
                list.Add(profile);
            }
            await SaveProfilesAsync(list);
        }

        public async Task DeleteProfileAsync(string id)
        {
            var list = await LoadProfilesAsync();
            list.RemoveAll(p => p.Id == id);
            await SaveProfilesAsync(list);
        }

        private static string SerializeProfiles(List<GitProfile> list)
        {
            var sb = new StringBuilder();
            sb.Append("[\n");
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                sb.Append("  {\n");
                sb.Append("    \"id\": ").Append(JsonStr(p.Id)).Append(",\n");
                sb.Append("    \"userName\": ").Append(JsonStr(p.UserName)).Append(",\n");
                sb.Append("    \"email\": ").Append(JsonStr(p.Email)).Append(",\n");
                sb.Append("    \"signingKey\": ").Append(JsonStr(p.SigningKey)).Append(",\n");
                sb.Append("    \"signingFormat\": ").Append(JsonStr(p.SigningFormat)).Append(",\n");
                sb.Append("    \"commitGpgsign\": ").Append(p.CommitGpgsign ? "true" : "false").Append(",\n");
                sb.Append("    \"tagGpgsign\": ").Append(p.TagGpgsign ? "true" : "false").Append(",\n");
                sb.Append("    \"sshKeyPath\": ").Append(JsonStr(p.SshKeyPath)).Append(",\n");
                sb.Append("    \"githubUsername\": ").Append(JsonStr(p.GithubUsername)).Append(",\n");
                sb.Append("    \"credentialHelper\": ").Append(JsonStr(p.CredentialHelper)).Append(",\n");
                sb.Append("    \"sslBackend\": ").Append(JsonStr(p.SslBackend)).Append(",\n");
                sb.Append("    \"createdAt\": ").Append(JsonStr(p.CreatedAt.ToString("o"))).Append(",\n");
                sb.Append("    \"lastUsedAt\": ").Append(JsonStr(p.LastUsedAt.ToString("o"))).Append("\n");
                sb.Append("  }");
                if (i < list.Count - 1) sb.Append(",");
                sb.Append("\n");
            }
            sb.Append("]\n");
            return sb.ToString();
        }

        private static string JsonStr(string s)
        {
            if (s == null) return "\"\"";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
        }

        private static List<GitProfile> ParseProfiles(string json)
        {
            var list = new List<GitProfile>();
            if (string.IsNullOrWhiteSpace(json)) return list;

            // 按对象块粗解析
            int i = 0;
            while (i < json.Length)
            {
                int start = json.IndexOf('{', i);
                if (start < 0) break;
                int end = json.IndexOf('}', start);
                if (end < 0) break;
                var block = json.Substring(start, end - start + 1);
                var p = ParseOne(block);
                if (p != null) list.Add(p);
                i = end + 1;
            }
            return list;
        }

        private static GitProfile ParseOne(string block)
        {
            try
            {
                var p = new GitProfile();
                p.Id = GetStr(block, "id") ?? p.Id;
                p.UserName = GetStr(block, "userName") ?? "";
                p.Email = GetStr(block, "email") ?? "";
                // 兼容旧数据：早期版本有独立的 name 字段，这里回退使用，避免显示空白
                if (string.IsNullOrEmpty(p.UserName))
                    p.UserName = GetStr(block, "name") ?? "";
                p.SigningKey = GetStr(block, "signingKey") ?? "";
                p.SigningFormat = GetStr(block, "signingFormat") ?? "openpgp";
                p.CommitGpgsign = GetBool(block, "commitGpgsign");
                p.TagGpgsign = GetBool(block, "tagGpgsign");
                p.SshKeyPath = GetStr(block, "sshKeyPath") ?? "";
                p.GithubUsername = GetStr(block, "githubUsername") ?? "";
                p.CredentialHelper = GetStr(block, "credentialHelper") ?? "";
                p.SslBackend = GetStr(block, "sslBackend") ?? "";
                var ca = GetStr(block, "createdAt");
                if (!string.IsNullOrEmpty(ca) && DateTime.TryParse(ca, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt1))
                    p.CreatedAt = dt1;
                var la = GetStr(block, "lastUsedAt");
                if (!string.IsNullOrEmpty(la) && DateTime.TryParse(la, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt2))
                    p.LastUsedAt = dt2;
                return p;
            }
            catch
            {
                return null;
            }
        }

        private static string GetStr(string block, string key)
        {
            var pattern = "\"" + key + "\"";
            int k = block.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (k < 0) return null;
            int colon = block.IndexOf(':', k + pattern.Length);
            if (colon < 0) return null;
            int q1 = block.IndexOf('"', colon + 1);
            if (q1 < 0) return null;
            int q2 = q1 + 1;
            while (q2 < block.Length)
            {
                if (block[q2] == '"' && block[q2 - 1] != '\\') break;
                q2++;
            }
            if (q2 >= block.Length) return null;
            var raw = block.Substring(q1 + 1, q2 - q1 - 1);
            return raw.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
        }

        private static bool GetBool(string block, string key)
        {
            var pattern = "\"" + key + "\"";
            int k = block.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (k < 0) return false;
            int colon = block.IndexOf(':', k + pattern.Length);
            if (colon < 0) return false;
            var rest = block.Substring(colon + 1).TrimStart();
            return rest.StartsWith("true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
