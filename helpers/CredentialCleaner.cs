using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GitConfigTool.Helpers
{
    /// <summary>
    /// Windows 凭据管理器中 gh 残留凭据的清理。
    /// gh auth logout 不会清理 keyring 里的 token，会留下孤儿凭据，这里按 gh 的登录列表反查并删除。
    /// 使用 Win32 凭据管理 API（advapi32.dll）。
    /// </summary>
    public static class CredentialCleaner
    {
        /// <summary>凭据目标名前缀</summary>
        private const string TargetPrefix = "gh:";

        /// <summary>
        /// 清理 gh 已不再登录的残留凭据。
        /// 有账号登录时，只删除能确定归属（gh:&lt;host&gt;:&lt;user&gt;）且该账号已不在登录列表中的条目，
        /// 无用户名条目（gh:&lt;host&gt;:）保留；一个账号都不存在时，清除该主机下的全部 gh 条目。
        /// </summary>
        /// <param name="loggedInUsers">gh 当前已登录的用户名集合</param>
        /// <returns>被删除的目标名列表</returns>
        public static List<string> CleanupOrphaned(ICollection<string> loggedInUsers)
        {
            var removed = new List<string>();
            bool noAccounts = loggedInUsers == null || loggedInUsers.Count == 0;

            foreach (var target in ListGhTargets())
            {
                // 一个账号都没有：该主机下所有 gh 凭据都是残留
                if (noAccounts)
                {
                    if (Delete(target)) removed.Add(target);
                    continue;
                }

                string host, user;
                if (!TryParseTarget(target, out host, out user)) continue;

                // 仍处于登录状态的账号不动；无用户名条目也无从判断，保留
                if (loggedInUsers.Contains(user)) continue;

                if (Delete(target)) removed.Add(target);
            }

            return removed;
        }

        /// <summary>
        /// 解析凭据目标名：gh:&lt;host&gt;:&lt;user&gt;
        /// 没有用户名部分的条目返回 false
        /// </summary>
        private static bool TryParseTarget(string target, out string host, out string user)
        {
            host = null;
            user = null;
            if (string.IsNullOrWhiteSpace(target)) return false;
            if (!target.StartsWith(TargetPrefix, StringComparison.OrdinalIgnoreCase)) return false;

            var rest = target.Substring(TargetPrefix.Length);
            var sep = rest.IndexOf(':');
            if (sep <= 0) return false;                 // 没有用户名部分是默认条目，不动

            host = rest.Substring(0, sep);
            user = rest.Substring(sep + 1);
            return host.Length > 0 && user.Length > 0;
        }

        /// <summary>
        /// 枚举当前用户的"普通凭据"，取出目标名以 gh: 开头的条目
        /// </summary>
        public static List<string> ListGhTargets()
        {
            var result = new List<string>();
            IntPtr handle = IntPtr.Zero;

            try
            {
                uint count;
                // 只枚举普通凭据（CRED_TYPE_GENERIC = 1）
                if (!CredEnumerate(null, 0, out count, out handle)) return result;
                if (handle == IntPtr.Zero || count == 0) return result;

                for (int i = 0; i < count; i++)
                {
                    var itemPtr = Marshal.ReadIntPtr(handle, i * IntPtr.Size);
                    if (itemPtr == IntPtr.Zero) continue;

                    var cred = (CREDENTIAL)Marshal.PtrToStructure(itemPtr, typeof(CREDENTIAL));
                    if (string.IsNullOrEmpty(cred.TargetName)) continue;
                    if (!cred.TargetName.StartsWith(TargetPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                    result.Add(cred.TargetName);
                }
            }
            catch
            {
                // 枚举失败时返回已收集到的部分，不抛异常影响界面
            }
            finally
            {
                if (handle != IntPtr.Zero) CredFree(handle);
            }

            return result;
        }

        /// <summary>
        /// 删除指定目标名的普通凭据
        /// </summary>
        private static bool Delete(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return false;

            // CRED_TYPE_GENERIC = 1，CRED_FLAGS 为 0
            return CredDelete(target, 1, 0);
        }

        #region Win32 凭据管理 API

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredEnumerate(
            string filter,
            uint flags,
            out uint count,
            out IntPtr credentials);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(
            string target,
            uint type,
            uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
        private static extern void CredFree(IntPtr buffer);

        /// <summary>
        /// CREDENTIAL 结构体（字符串字段交给封送器按 Unicode 处理）
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string TargetName;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string Comment;

            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string TargetAlias;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string UserName;
        }

        #endregion
    }
}
