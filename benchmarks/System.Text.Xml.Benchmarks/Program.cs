using BenchmarkDotNet.Running;
using System.Text.Xml.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(ReadingBenchmarks).Assembly).Run(args);
