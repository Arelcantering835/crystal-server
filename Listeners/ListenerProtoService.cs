using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Server.Core;

namespace Server.Listeners;

[Authorize]
public sealed class ListenerProtoService(
    ILogger<ListenerProtoService> logger, IRepository<Listener> db, IServiceScopeFactory factory,
    ListenerManager manager, IBroker<ListenerEvent> broker) : ListenerService.ListenerServiceBase
{
    public override async Task<Empty> CreateListener(CreateListenerRequest request, ServerCallContext context)
    {
        // sanity check things are not already in use
        var listeners = manager.List();

        if (listeners.Any(g => g.BindPort == request.BindPort))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "BindPort is in use"));

        // create an entity to store in the db
        var listener = Listener.Create(request.Name, (ushort)request.BindPort, request.CoffName, request.CoffBytes.ToByteArray());

        // create a running instance
        var instance = new ListenerInstance(listener.Id, listener.BindPort, factory);

        // try to start it
        try
        {
            instance.Start();
            listener.Status = instance.Status;
        }
        catch (Exception e)
        {
            throw new RpcException(new Status(StatusCode.Unknown, e.Message));
        }

        // store the running instance
        manager.Add(listener.Id, instance);

        // store the db entity
        await db.AddAsync(listener, context.CancellationToken);
        await db.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation("Started listener {NAME} on port {PORT}", listener.Name, request.BindPort);
        
        // publish an event
        broker.Publish(new ListenerEvent
        {
            Listener = ToListenerInfo(listener),
            Type = ListenerEventType.ListenerEventAdded
        });

        return new Empty();
    }

    public override async Task ListenerEvents(Empty request, IServerStreamWriter<ListenerEvent> responseStream, ServerCallContext context)
    {
        // stream first bunch from the db
        var listeners = await db.ListAsync(context.CancellationToken);

        foreach (var listener in listeners)
        {
            await responseStream.WriteAsync(new ListenerEvent
            {
                Listener = ToListenerInfo(listener),
                Type = ListenerEventType.ListenerEventAdded
            });
        }

        // set up a pub/sub to get notifications of new creations and deletions
        var (reader, subscription) = broker.Subscribe();

        using (subscription)
        {
            await foreach (var listenerEvent in reader.ReadAllAsync(context.CancellationToken))
            {
                await responseStream.WriteAsync(listenerEvent, context.CancellationToken);
            }
        }
    }

    public override async Task<Empty> DeleteListener(DeleteListenerRequest request, ServerCallContext context)
    {
        var listener = await db.GetByIdAsync(request.Id, context.CancellationToken);

        if (listener is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Listener not found"));
        
        // delete from db
        await db.DeleteAsync(listener, context.CancellationToken);
        
        // kill the running instance
        await manager.Remove(listener.Id);
        
        logger.LogInformation("Removed listener {NAME}", listener.Name);
        
        // publish an event
        broker.Publish(new ListenerEvent
        {
            Listener = ToListenerInfo(listener),
            Type = ListenerEventType.ListenerEventDeleted
        });
        
        return new Empty();
    }

    private static ListenerInfo ToListenerInfo(Listener listener)
    {
        return new ListenerInfo
        {
            Id = listener.Id,
            Name = listener.Name,
            BindPort = listener.BindPort,
            CoffName = listener.CoffName,
            CoffBytes = ByteString.CopyFrom(listener.Coff),
            Status = listener.Status,
            PublicKey = ByteString.CopyFrom(listener.PublicKey)
        };
    }
}