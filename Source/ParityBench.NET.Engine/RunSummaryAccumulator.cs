using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Engine;

    public class RunSummaryAccumulator
    {
        private int totalPairs;
        private int equalPairs;
        private int differentPairs;
        private int errorPairs;
        private int statusCodeMismatchPairs;
        private int bothNonSuccessPairs;

        public void Add(IEnumerable<RequestPairResult> results)
        {
            foreach (RequestPairResult result in results)
            {
                totalPairs++;
                switch (result.Outcome)
                {
                    case RequestPairOutcome.Equal:
                        equalPairs++;
                        break;
                    case RequestPairOutcome.Different:
                        differentPairs++;
                        break;
                    case RequestPairOutcome.ExecutionFailed:
                        errorPairs++;
                        break;
                    case RequestPairOutcome.StatusCodeMismatch:
                        statusCodeMismatchPairs++;
                        break;
                    case RequestPairOutcome.BothNonSuccess:
                        bothNonSuccessPairs++;
                        break;
                }
            }
        }

        public RunResultSummary ToSummary(
            RunDetailReference detailReference,
            RunExecutionMetrics executionMetrics) =>
            new RunResultSummary(
                totalPairs,
                equalPairs,
                differentPairs,
                errorPairs,
                statusCodeMismatchPairs,
                bothNonSuccessPairs,
                detailReference,
                executionMetrics);
    }
