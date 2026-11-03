namespace ThroughputTest;

sealed class Observable<T>(T initial) where T : notnull
{
    private readonly object sync = new();
    private T value = initial;
    public event Action<T>? Changed;
    public T Value
    {
        get { lock (sync) return value; }
        set
        {
            lock (sync)
            {
                if (EqualityComparer<T>.Default.Equals(this.value, value)) return;
                this.value = value;
                Changed?.Invoke(value);
            }
        }
    }
}
