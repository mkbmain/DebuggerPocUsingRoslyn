using System;
using System.Collections.Generic;
using System.Threading;

namespace DebuggerLib
{
    public static class DebugHelper
    {
        public static event Action<int, int, Var[]> BreakPointHit;
        public static Dictionary<int, bool> BreakPoints = new Dictionary<int, bool>();
        public static bool Lock = false;

        public static void BreakPoint(int spanStart, int spanLength, params Var[] variables)
        {
            if (BreakPointHit is null || !BreakPoints.TryGetValue(spanStart, out var breakPoint) || !breakPoint) return;

            Lock = true;
            BreakPointHit.Invoke(spanStart, spanLength, variables);
            while (Lock)
            {
                Thread.Sleep(505);
            }
        }
    }

    public struct Var
    {
        public string Name;
        public object Value;

        public Var(string name, object value)
        {
            Name = name;
            Value = value;
        }
    }
}