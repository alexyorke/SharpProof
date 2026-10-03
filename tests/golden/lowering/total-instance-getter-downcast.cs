// golden-mode: Total
// golden-inline-source: true
// A nonvirtual getter inlines on its receiver, which is null-checked. Inside
// it, `base.Parent` reads the base backing field and the downcast keeps the
// reference behind an approximated InvalidCast guard. Generic container types
// bridge to the caller's by casts.
public class Node<TKey>
{
    private Node<TKey> _parent;
    public virtual Node<TKey> Parent { get { return this._parent; } }
}
public class RedNode<TKey> : Node<TKey>
{
    public new RedNode<TKey> Parent { get { return (RedNode<TKey>)base.Parent; } }
}
public class Map<TKey>
{
    public RedNode<TKey> Target(RedNode<TKey> node)
    {
        return node == null ? null : node.Parent;
    }
}
