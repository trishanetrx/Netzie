namespace NetSentry.Models
{
    public class BlockedRuleInfo
    {
        public string RuleName { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string Type { get; set; } = "Program";
        public bool Enabled { get; set; } = true;
        public string Action { get; set; } = "Block";
        public string Direction { get; set; } = "Outbound";
    }
}
