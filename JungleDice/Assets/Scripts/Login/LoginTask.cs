using System;
using System.Collections;

namespace JungleDice.Login
{
    public readonly struct LoginTask
    {
        public readonly string Name;
        public readonly Func<IEnumerator> Run;

        public LoginTask(string name, Func<IEnumerator> run)
        {
            Name = name;
            Run = run;
        }
    }
}
