using System;
using System.Collections;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;

namespace PulletFramework.NetClient.Tests
{
    // Unity Test Framework 1.1 uses IEnumerator, not NUnit async Task tests.
    // Yield to the real Unity context instead of blocking its continuations.
    internal static class UnityTaskTest
    {
        public static IEnumerator Run(Func<Task> body)
        {
            Task task = body();
            var watch = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(10), "Async test did not complete; original operation deadlines are unchanged.");
                yield return null;
            }
            task.GetAwaiter().GetResult();
        }
    }
}
