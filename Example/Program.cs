using System;
using System.Collections.Generic;
using System.Linq;

namespace Example
{
    class Program
    {
        static void Main(string[] args)
        {
            // Square root by the Newton's method.
            var a = 5.0;
            var x = a;
            Run(a, x);
        }


        static void Run(double a, double x)
        {
            var list = new List<double>();
            for (var i = 0; i < 100; i++)
            {
                var xi = (x + a / x) / 2;
                list.Add(xi);
                if (x == xi) break;
                x = xi;
            }

            Console.WriteLine(x);
            Console.WriteLine(list.Aggregate("",(a,b)=>$"{a}{b}"));
        }
    }
}