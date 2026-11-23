namespace DoraFood.Models.Entities
{
    public sealed class FaqItem
    {
        public long Id { get; set; }

        public string Question { get; set; } = string.Empty;

        public string Answer { get; set; } = string.Empty;

        public int SortOrder { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; }

        public byte[] Version { get; set; } = null!;
    }
}
