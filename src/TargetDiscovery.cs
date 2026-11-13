using System;
using System.Collections.Generic;
using System.Linq;

namespace PlaqueRamen;

// Keep the load/placement retries, scoped to the affected decoration. Ordinary
// furniture placement must not rescan every mesh and renderer in the scene.
internal sealed class TargetDiscovery<T> where T : notnull
{
    private readonly Dictionary<T,Queue<double>> _pending=new();
    internal int Count=>_pending.Count;
    internal void Request(T target,double now)
    { _pending[target]=new Queue<double>(new[]{now,now+.25,now+1,now+4,now+12}); }
    internal T[] Due(double now)
    {
        List<T>? due=null,finished=null;
        foreach(var entry in _pending)
        {
            if(now<entry.Value.Peek())continue;
            entry.Value.Dequeue();(due??=new()).Add(entry.Key);
            if(entry.Value.Count==0)(finished??=new()).Add(entry.Key);
        }
        if(finished!=null)foreach(var target in finished)_pending.Remove(target);
        return due?.ToArray()??Array.Empty<T>();
    }
    internal void RemoveWhere(Predicate<T> predicate)
    {foreach(var key in _pending.Keys.Where(k=>predicate(k)).ToArray())_pending.Remove(key);}
}
