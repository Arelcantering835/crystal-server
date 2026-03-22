using Server.Core;
using Server.Utilities;

namespace Server.Tasks;

public sealed class BeaconTask : Entity
{
    public uint BeaconId { get; set; }
    public TaskType TaskType { get; set; }
    public byte[]? TaskData { get; set; }
    public string? CommandLine { get; set; }
    public string? User { get; set; }
    public TaskStatus Status { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    
    public DateTimeOffset? EndTime { get; set; }

    public static BeaconTask Create(TaskRequest request, string? user)
    {
        return new BeaconTask
        {
            Id = Helpers.GenerateId(),
            BeaconId = request.BeaconId,
            TaskType = request.TaskType,
            TaskData = request.TaskData.ToByteArray(),
            CommandLine = request.CommandLine,
            User = user,
            Status = TaskStatus.Pending
        };
    }

    public void SetTasked()
    {
        Status = TaskStatus.Tasked;
        StartTime = DateTimeOffset.UtcNow;
    }

    public void SetComplete()
    {
        Status = TaskStatus.Complete;
        EndTime = DateTimeOffset.UtcNow;
    }
}