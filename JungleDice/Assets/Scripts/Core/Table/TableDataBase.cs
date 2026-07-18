using System;

namespace JungleDice.Core.Table
{
    [Serializable]
    public abstract class TableDataBase<TKey>
    {
        public abstract TKey Key { get; }
    }
}
