// golden-mode: Total
// golden-inline-source: true
// golden-opaque-calls: true
// A generic ring buffer's insert: a covariant element store, field updates,
// a field increment and a source getter over the array's length.
public class Buffer<T>
{
    private T[] _items = new T[2];
    private int _end;
    private int _start;
    private int _count;
    public int Length { get { return _items.Length - 1; } }
    public void Target(T value)
    {
        _items[_end] = value;
        _end = (_end + 1) % _items.Length;
        if (_end == _start)
        {
            _start = (_start + 1) % _items.Length;
        }
        _count = _count < Length ? ++_count : _count;
    }
}
