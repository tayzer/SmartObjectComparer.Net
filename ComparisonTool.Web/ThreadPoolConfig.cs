// <copyright file="ThreadPoolConfig.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace ComparisonTool.Web
{
    using System;
    using System.Threading;

    public static class ThreadPoolConfig
    {
        public static void Configure()
        {
            var minWorkerThreads = Math.Max(Environment.ProcessorCount * 8, 200);
            var minCompletionPortThreads = Math.Max(Environment.ProcessorCount * 2, 32);
            ThreadPool.SetMinThreads(minWorkerThreads, minCompletionPortThreads);
        }
    }
}
