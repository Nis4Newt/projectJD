using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using JungleDice.Core.Table;
using UnityEditor;
using UnityEngine;

namespace JungleDice.Core.Table.Editor
{
    internal static class TableGenerator
    {
        private const string SourceDir = "Assets/Tables/Source";
        private const string OutputDir = "Assets/Resources/Tables";

        [MenuItem("Tools/Table/Generate All Tables")]
        public static void GenerateAllTables()
        {
            Directory.CreateDirectory(SourceDir);
            Directory.CreateDirectory(OutputDir);

            var txtFiles = Directory.GetFiles(SourceDir, "*.txt", SearchOption.TopDirectoryOnly);
            int success = 0, failed = 0;

            foreach (var path in txtFiles)
            {
                var tableName = Path.GetFileNameWithoutExtension(path);
                if (TryGenerateTable(tableName, path))
                    success++;
                else
                    failed++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[TableGenerator] 완료: 성공 {success}, 실패 {failed}");
        }

        /// 이미 존재하는 테이블 asset 하나만 자신의 원본 텍스트에서 다시 읽어들인다.
        /// TableAssetEditor의 "텍스트에서 다시 로드" 버튼에서 호출.
        public static bool ReloadTable(TableAssetBase asset)
        {
            var tableName = asset.GetType().Name;
            var path = $"{SourceDir}/{tableName}.txt";

            if (!File.Exists(path))
            {
                Debug.LogError($"[TableGenerator] '{tableName}' 원본 텍스트를 찾을 수 없음: {path}");
                return false;
            }

            if (!TryReadLines(path, out var headers, out var rows))
                return false;

            asset.PopulateFromText(headers, rows);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TableGenerator] '{tableName}' 다시 로드 완료 ({rows.Count}행)");
            return true;
        }

        private static bool TryGenerateTable(string tableName, string path)
        {
            var type = FindTableType(tableName);
            if (type == null)
            {
                Debug.LogError($"[TableGenerator] '{tableName}'에 대응하는 변환 클래스를 찾을 수 없음 (ITableAsset 구현 + ScriptableObject 상속 + 클래스명 == 파일명 필요)");
                return false;
            }

            if (!TryReadLines(path, out var headers, out var rows))
                return false;

            var assetPath = $"{OutputDir}/{tableName}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance(type);
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            ((ITableAsset)asset).PopulateFromText(headers, rows);
            EditorUtility.SetDirty(asset);
            return true;
        }

        private static bool TryReadLines(string path, out string[] headers, out List<string[]> rows)
        {
            headers = null;
            rows = null;

            var lines = File.ReadAllLines(path, System.Text.Encoding.UTF8)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();

            if (lines.Length == 0)
            {
                Debug.LogError($"[TableGenerator] '{Path.GetFileNameWithoutExtension(path)}' 파일이 비어있음");
                return false;
            }

            headers = lines[0].Split('|').Select(h => h.Trim()).ToArray();
            rows = lines.Skip(1)
                .Select(l => l.Split('|').Select(c => c.Trim()).ToArray())
                .ToList();
            return true;
        }

        private static Type FindTableType(string tableName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => SafeGetTypes(a))
                .FirstOrDefault(t =>
                    t.Name == tableName &&
                    typeof(ScriptableObject).IsAssignableFrom(t) &&
                    typeof(ITableAsset).IsAssignableFrom(t));
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
        }
    }
}
