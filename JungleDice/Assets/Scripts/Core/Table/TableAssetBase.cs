using System.Collections.Generic;
using UnityEngine;

namespace JungleDice.Core.Table
{
    public abstract class TableAssetBase : ScriptableObject, ITableAsset
    {
        // 실제 구현은 TableBase<TSelf,TData,TKey>가 담당 (TData/TKey 제네릭 정보가 필요)
        public abstract void PopulateFromText(string[] headers, List<string[]> rows);
    }
}
