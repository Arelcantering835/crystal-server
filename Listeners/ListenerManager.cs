using System.Collections.Concurrent;

namespace Server.Listeners;

public sealed class ListenerManager
{
    private readonly ConcurrentDictionary<uint, ListenerInstance> _gates = [];

    public void Add(uint id, ListenerInstance listener)
    {
        _gates.AddOrUpdate(id, listener, (_, _) => listener);
    }

    public List<ListenerInstance> List()
    {
        return _gates.Values.ToList();
    }

    public Task Remove(uint id)
    {
        return _gates.TryRemove(id, out var gate)
            ? gate.Stop()
            : Task.CompletedTask;
    }
}