namespace ErsatzTV.Core.Domain.CopyPrep;

public class CopyPrepQueueLogEntry
{
    public int Id { get; set; }

    public int CopyPrepQueueItemId { get; set; }
    public CopyPrepQueueItem CopyPrepQueueItem { get; set; }

    public DateTime CreatedAt { get; set; }
    public string Level { get; set; }
    public string Event { get; set; }
    public string Message { get; set; }
    public string Details { get; set; }
}
