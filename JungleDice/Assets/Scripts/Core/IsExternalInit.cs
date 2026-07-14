// Unity의 .NET Standard 2.1 환경에서 C# record 타입 사용을 위한 폴리필.
// .NET 5+ 에는 내장되어 있으나 Unity corlib에 포함되지 않아 직접 정의 필요.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
