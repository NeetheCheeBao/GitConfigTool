using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GitConfigTool.Helpers
{
    /// <summary>
    /// 把文件移到回收站，而不是直接删除。
    /// </summary>
    public static class RecycleBin
    {
        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOERRORUI = 0x0400;

        /// <summary>
        /// 把给定文件移到回收站，返回是否全部成功。
        /// </summary>
        public static bool MoveToRecycleBin(IEnumerable<string> paths)
        {
            var list = new List<string>();
            foreach (var path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path)) list.Add(path);
            }
            if (list.Count == 0) return false;

            // pFrom 要求「双 null 结尾的路径列表」：Join 后补一个 \0，封送器还会再加一个
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = string.Join("\0", list) + "\0",
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
            };

            try
            {
                return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
            }
            catch (Exception)
            {
                return false;
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }
    }
}
