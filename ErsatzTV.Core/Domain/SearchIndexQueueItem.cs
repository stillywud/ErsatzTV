namespace ErsatzTV.Core.Domain;

public class SearchIndexQueueItem
{
    public int Id { get; set; }
    public int MediaItemId { get; set; }
    public SearchIndexOperation Operation { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool Processed { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
