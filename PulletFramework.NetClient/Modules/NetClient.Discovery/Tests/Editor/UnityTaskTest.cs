using System;
using System.Collections;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;
namespace PulletFramework.NetClient.Discovery.Tests
{
    internal static class UnityTaskTest
    {
        internal static IEnumerator Run(Func<Task> body)
        {
            var task = body(); var clock = Stopwatch.StartNew();
            while (!task.IsCompleted) { Assert.That(clock.Elapsed.TotalSeconds, Is.LessThan(10)); yield return null; }
            task.GetAwaiter().GetResult();
        }
    }
}
