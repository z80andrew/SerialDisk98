using System.Collections.Generic;
using System.Threading;

namespace SerialDisk98.Utilities
{
    public static class ThreadManager
    {
        private static readonly List<Thread> _threads = new List<Thread>();

        public static void AddThread(Thread newThread)
        {
            _threads.Add(newThread);
            _threads.Find(thread => thread == newThread).Start();
        }
    }
}
