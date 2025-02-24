using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Reqnroll.Tracing;
using Xunit;

namespace Reqnroll.RuntimeTests
{
    public class TraceListenerQueueTests
    {
        [Theory(DisplayName ="EnqueueMessage n times should yield messages synchronously")]
        [InlineData(2)]
        [InlineData(32)]
        [InlineData(128)]
        public void EnqueueMessage_nTimes_ShouldBeSynchronous(int times)
        {
            // ARRANGE
            var countdown = new CountdownEvent(times);
            var semaphore = new SemaphoreSlim(1, 1);
            var testOutputList = new ConcurrentBag<string>();

            bool failureOnSemaphoreEntering = false;

            

            var traceListenerMock = Substitute.For<ITraceListener>();
            traceListenerMock.When(m => m.WriteTestOutput(Arg.Any<string>()))
                             .Do(args => WriteTestOutputCallback(args.Arg<string>()));

            var testRunnerManagerMock = GetTestRunnerManagerMock();
            var testRunnerMock = GetTestRunnerMock();
            var traceListenerQueue = new TraceListenerQueue(traceListenerMock, testRunnerManagerMock);

            // ACT
            Parallel.For(0, times, i => traceListenerQueue.EnqueueMessage(testRunnerMock, $"No. {i} - Thread {Thread.CurrentThread.ManagedThreadId}", false));

            // ASSERT
            countdown.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
            failureOnSemaphoreEntering.Should().BeFalse();
            testOutputList.Should().HaveCount(times);



            void WriteTestOutputCallback(string message)
            {
                if (!semaphore.Wait(0))
                {
                    failureOnSemaphoreEntering = true;
                    return;
                }

                testOutputList.Add(message);
                semaphore.Release();
                countdown.Signal();
            }
        }

        private ITestRunner GetTestRunnerMock()
        {
            var testRunnerMock = Substitute.For<ITestRunner>();
            testRunnerMock.TestWorkerId
                          .Returns(_ => Thread.CurrentThread.ManagedThreadId.ToString());
            return testRunnerMock;
        }

        private ITestRunnerManager GetTestRunnerManagerMock()
        {
            var testRunnerManagerMock = Substitute.For<ITestRunnerManager>();
            testRunnerManagerMock.IsMultiThreaded
                                 .Returns(true);
            return testRunnerManagerMock;
        }
    }
}
