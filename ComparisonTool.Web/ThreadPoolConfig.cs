using System;
using System.Threading;

namespace ComparisonTool.Web
{
    public static class ThreadPoolConfig
    {
        public static void Configure()
        {
            int minWorkerThreads = Math.Max(Environment.ProcessorCount * 8, 200);
            int minCompletionPortThreads = Math.Max(Environment.ProcessorCount * 2, 32);
            ThreadPool.SetMinThreads(minWorkerThreads, minCompletionPortThreads);
        }
    }
}
