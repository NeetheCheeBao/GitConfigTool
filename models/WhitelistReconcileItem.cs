namespace GitConfigTool.Models
{
    /// <summary>白名单对账结果的两类问题</summary>
    public enum WhitelistReconcileKind
    {
        /// <summary>本机有这把公钥，但白名单里没有，建议加入</summary>
        Missing,

        /// <summary>白名单里有这条，但本机找不到对应密钥对</summary>
        Stale
    }

    /// <summary>
    /// 白名单对账里的一条待处理项。
    /// </summary>
    public class WhitelistReconcileItem
    {
        /// <summary>问题类型</summary>
        public WhitelistReconcileKind Kind { get; set; }

        /// <summary>是否勾选（界面上的复选框）</summary>
        public bool IsSelected { get; set; }

        /// <summary>是否可以自动处理；false 时复选框禁用</summary>
        public bool CanApply { get; set; }

        /// <summary>主标题：密钥名，或白名单里的标识</summary>
        public string Title { get; set; }

        /// <summary>副标题：说明这一条为什么被列出来</summary>
        public string Detail { get; set; }

        /// <summary>额外提醒，没有时为 null（界面隐藏该行）</summary>
        public string Note { get; set; }

        /// <summary>标识：加入白名单时写在行首</summary>
        public string Principal { get; set; }

        /// <summary>算法名，加入白名单时用</summary>
        public string Algorithm { get; set; }

        /// <summary>公钥 base64 段，加入白名单时用</summary>
        public string PublicKeyBlob { get; set; }

        /// <summary>白名单里的原始整行，删除时用它精确匹配</summary>
        public string RawLine { get; set; }
    }
}
