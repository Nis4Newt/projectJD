using System.Collections.Generic;

namespace JungleDice.Core.Table
{
    public interface ITableAsset
    {
        void PopulateFromText(string[] headers, List<string[]> rows);
    }
}
