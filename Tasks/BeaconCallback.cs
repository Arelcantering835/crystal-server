using System.Buffers.Binary;

namespace Server.Tasks;

/// <summary>
/// BeaconOutput from a Beacon
/// </summary>
public sealed class BeaconCallback
{
    public int Type { get; set; }
    public uint TaskId { get; set; }
    public byte[]? Output { get; set; }
    public TaskStatus Status { get; set; }

    public static BeaconCallback Parse(byte[] data)
    {
        var taskOutput = new BeaconCallback();
        var offset = 0;

        taskOutput.TaskId = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, sizeof(uint)));
        offset += sizeof(uint);
        
        taskOutput.Type = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, sizeof(int)));
        offset += sizeof(int);
        
        var length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, sizeof(int)));
        offset += sizeof(int);

        if (length > 0)
        {
            var output = data.AsSpan().Slice(offset, length);
            taskOutput.Output = output.ToArray();
        }
        
        offset += length;
        
        var complete = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, sizeof(int)));

        if (complete == 1)
            taskOutput.Status = TaskStatus.Complete;
        
        return taskOutput;
    }
}