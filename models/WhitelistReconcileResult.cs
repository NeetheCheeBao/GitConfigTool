using System.Collections.Generic;
using System.Linq;

namespace GitConfigTool.Models
{
    /// <summary>
    /// 白名单对账的完整结果，供界面分区显示。
    /// </summary>
    public class WhitelistReconcileResult
    {
        /// <summary>全部待处理项</summary>
        public List<WhitelistReconcileItem> Items { get; set; } = new List<WhitelistReconcileItem>();

        /// <summary>建议加入白名单的项</summary>
        public List<WhitelistReconcileItem> Missing =>
            Items.Where(i => i.Kind == WhitelistReconcileKind.Missing).ToList();

        /// <summary>白名单里本机找不到密钥的项</summary>
        public List<WhitelistReconcileItem> Stale =>
            Items.Where(i => i.Kind == WhitelistReconcileKind.Stale).ToList();

        public bool HasMissing => Items.Any(i => i.Kind == WhitelistReconcileKind.Missing);

        public bool HasStale => Items.Any(i => i.Kind == WhitelistReconcileKind.Stale);

        /// <summary>没有任何问题</summary>
        public bool IsEmpty => Items.Count == 0;
    }
}
