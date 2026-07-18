using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace JungleDice.Core.Table
{
    public static class TableLoader
    {
        public static IEnumerator LoadAllRoutine()
        {
            int count = 0;
            foreach (var type in FindAllTableTypes())
            {
                var instanceProperty = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                instanceProperty?.GetValue(null); // getter 내부에서 Resources.Load + OnLoaded() 수행 (실패해도 LogError만, 예외 없음)
                count++;
                yield return null;
            }

            Debug.Log($"[TableLoader] 테이블 로드 완료 ({count}개)");
        }

        private static IEnumerable<Type> FindAllTableTypes()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(SafeGetTypes)
                .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && typeof(TableAssetBase).IsAssignableFrom(t));
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
        }
    }
}
