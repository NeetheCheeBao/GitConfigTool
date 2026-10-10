using System;

namespace GitConfigTool.Models
{
    /// <summary>
    /// 日志条目类型（供界面按类型着色）
    /// </summary>
    public enum LogKind
    {
        /// <summary>普通提示信息</summary>
        Info,

        /// <summary>执行的命令</summary>
        Command,

        /// <summary>命令的原始输出（弱化显示）</summary>
        Result,

        /// <summary>成功结果</summary>
        Success,

        /// <summary>错误信息</summary>
        Error
    }

    /// <summary>
    /// 命令与输出记录的一行
    /// </summary>
    public class LogEntry
    {
        /// <summary>产生时间</summary>
        public DateTime Time { get; set; } = DateTime.Now;

        /// <summary>条目类型</summary>
        public LogKind Kind { get; set; } = LogKind.Info;

        /// <summary>命令条目显示为「执行:」，其他类型为空</summary>
        public string Label => Kind == LogKind.Command ? "执行:" : string.Empty;

        /// <summary>正文内容</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>时间戳文本</summary>
        public string TimeText => Time.ToString("HH:mm:ss");

        public override string ToString() => "[" + TimeText + "] " + Label + " " + Text;
    }
}
