namespace PPPaint.Core;

public sealed class History<TState>
{
    private readonly int _capacity;
    private readonly Stack<TState> _undo = new();
    private readonly Stack<TState> _redo = new();

    public History(int capacity = 30)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Capture(TState state)
    {
        _undo.Push(state);
        _redo.Clear();
        TrimOldest(_undo);
    }

    public bool TryUndo(TState current, out TState state)
    {
        if (!_undo.TryPop(out state!)) return false;
        _redo.Push(current);
        return true;
    }

    public bool TryRedo(TState current, out TState state)
    {
        if (!_redo.TryPop(out state!)) return false;
        _undo.Push(current);
        TrimOldest(_undo);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private void TrimOldest(Stack<TState> stack)
    {
        if (stack.Count <= _capacity) return;
        var retained = stack.Take(_capacity).Reverse().ToArray();
        stack.Clear();
        foreach (var item in retained) stack.Push(item);
    }
}
