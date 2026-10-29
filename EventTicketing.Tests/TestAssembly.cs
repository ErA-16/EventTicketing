using Xunit;
using Xunit.Sdk;

[assembly: Xunit.v3.Parallelization(Mode = ParallelMode.None)]
[assembly: AssemblyFixture(typeof(EventTicketing.Tests.Infrastructure.ApiFixture))]
