using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Server.Core;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;

namespace Server.Tasks;

[Authorize]
public sealed class TasksProtoService(IRepository<BeaconTask> tasks, TaskBroker taskBroker)
    : TaskService.TaskServiceBase
{
    public override async Task StreamTasks(IAsyncStreamReader<TaskRequest> requestStream,
        IServerStreamWriter<TaskResponse> responseStream, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        var user = context.GetHttpContext().User;

        // One channel per connection — all task outputs for this client flow through here.
        var clientChannel = Channel.CreateUnbounded<BeaconCallback>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        // Read incoming TaskRequests concurrently with the response write loop below.
        var readLoop = ReadRequestsAsync(requestStream, responseStream, clientChannel, user.Identity?.Name, ct);

        // Forward task output to the client as TaskResponse messages.
        await foreach (var output in clientChannel.Reader.ReadAllAsync(ct))
            await responseStream.WriteAsync(ToResponse(output), ct);

        await readLoop;
    }

    private async Task ReadRequestsAsync(IAsyncStreamReader<TaskRequest> requestStream,
        IServerStreamWriter<TaskResponse> responseStream, Channel<BeaconCallback> clientChannel, string? name, CancellationToken ct)
    {
        var registrations = new List<IDisposable>();

        try
        {
            while (await requestStream.MoveNext(ct))
            {
                var beaconTask = BeaconTask.Create(requestStream.Current, name);

                await tasks.AddAsync(beaconTask, ct);
                await tasks.SaveChangesAsync(ct);

                registrations.Add(taskBroker.Register(beaconTask.Id, clientChannel.Writer));

                // send a 'pending' back straight away
                await responseStream.WriteAsync(new TaskResponse
                {
                    TaskId = beaconTask.Id,
                    Status = TaskStatus.Pending,
                    Output = ByteString.Empty,
                    Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
                }, ct);
            }
        }
        finally
        {
            // Unregister all task IDs for this client and signal the response loop to finish.
            foreach (var reg in registrations)
                reg.Dispose();

            clientChannel.Writer.TryComplete();
        }
    }

    private static TaskResponse ToResponse(BeaconCallback output) => new()
    {
        TaskId = output.TaskId,
        Status = output.Status,
        Output = output.Output is not null ? ByteString.CopyFrom(output.Output) : null,
        Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
    };
}