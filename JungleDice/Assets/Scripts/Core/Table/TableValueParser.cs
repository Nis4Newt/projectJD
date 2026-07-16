using System;
using System.Globalization;

namespace JungleDice.Core.Table
{
    internal static class TableValueParser
    {
        public static bool TryParse(Type type, string raw, out object value)
        {
            raw = raw.Trim();
            try
            {
                if (type == typeof(string)) { value = raw; return true; }
                if (type.IsEnum) { value = Enum.Parse(type, raw, true); return true; }
                if (type == typeof(bool)) { value = raw is "1" or "true" or "True" or "TRUE"; return true; }

                value = Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }
    }
}
