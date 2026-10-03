// golden-mode: Total
// golden-opaque-calls: true
// A catch-all handles dispatched source calls, struct getters and a
// constrained call on a type-parameter value with a boxed argument.
using System.Collections.Generic;
public class Tree<TKey, TValue>
{
    public virtual KeyValuePair<TKey, TValue> Find(TKey key) { throw new System.Exception(); }
}
public class Map<TKey, TValue>
{
    private Tree<TKey, TValue> _collection { get; set; }
    public bool Target(KeyValuePair<TKey, TValue> item)
    {
        try
        {
            var entry = _collection.Find(item.Key);
            return entry.Value.Equals(item.Value);
        }
        catch (System.Exception)
        {
            return false;
        }
    }
}
