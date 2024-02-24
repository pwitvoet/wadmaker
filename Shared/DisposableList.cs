namespace Shared
{
    /// <summary>
    /// A list of disposable items. Disposing this list will dispose and remove its contents.
    /// </summary>
    public class DisposableList<T> : List<T>, IDisposable
        where T : IDisposable
    {
        public DisposableList()
            : base()
        {
        }

        public DisposableList(IEnumerable<T> collection)
            : base(collection)
        {
        }

        public DisposableList(int capacity)
            : base(capacity)
        {
        }

        public void Dispose()
        {
            var exceptions = new List<Exception>();

            foreach (var item in this)
            {
                try
                {
                    item?.Dispose();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }

            if (exceptions.Count > 0)
                throw new AggregateException(exceptions);

            Clear();
        }
    }
}
