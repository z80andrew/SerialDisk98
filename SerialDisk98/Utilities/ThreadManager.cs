using System.Collections.Generic;
using System.Threading;

namespace SerialDiskXtreme.Utilities
{
    public static class ThreadManager
    {
        private static List<Thread> _threads = new List<Thread>();

        public static void AddThread(Thread newThread)
        {
            _threads.Add(newThread);
            _threads.Find(thread => thread == newThread).Start();
        }
    }
}
