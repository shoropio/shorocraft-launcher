using System;
using CmlLib.Core;

namespace TestCml
{
    class Program
    {
        static void Main(string[] args)
        {
            var p = new MinecraftPath();
            var l = new MinecraftLauncher(p);
        }
    }
}
