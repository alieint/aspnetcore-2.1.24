// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    public class PipeThroughputBenchmark
    {
        private const int _writeLenght = 57;
        private const int InnerLoopCount = 512;

        private Pipe _pipe;
        private MemoryPool<byte> _memoryPool;

        [IterationSetup]
        public void Setup()
        {
            _memoryPool = KestrelMemoryPool.Create();
            _pipe = new Pipe(new PipeOptions(_memoryPool));
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public void ParseLiveAspNetTwoTasks()
        {
            var writing = Task.Run(async () =>
            {
                for (int i = 0; i < InnerLoopCount; i++)
                {
                    _pipe.Writer.GetMemory(_writeLenght);
                    _pipe.Writer.Advance(_writeLenght);
                    await _pipe.Writer.FlushAsync();
                }
            });

            var reading = Task.Run(async () =>
            {
                long remaining = InnerLoopCount * _writeLenght;
                while (remaining != 0)
                {
                    var result = await _pipe.Reader.ReadAsync();
                    remaining -= result.Buffer.Length;
                    _pipe.Reader.AdvanceTo(result.Buffer.End, result.Buffer.End);
                }
            });

            Task.WaitAll(writing, reading);
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public void ParseLiveAspNetInline()
        {
            for (int i = 0; i < InnerLoopCount; i++)
            {
                _pipe.Writer.GetMemory(_writeLenght);
                _pipe.Writer.Advance(_writeLenght);
                _pipe.Writer.FlushAsync().GetAwaiter().GetResult();
                var result = _pipe.Reader.ReadAsync().GetAwaiter().GetResult();
                _pipe.Reader.AdvanceTo(result.Buffer.End, result.Buffer.End);
            }
        }

        [IterationCleanup]
        public void Cleanup()
        {
            _memoryPool.Dispose();
        }
    }
}
