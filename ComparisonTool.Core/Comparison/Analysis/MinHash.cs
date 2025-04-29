using System;
using System.Collections.Generic;
using System.Linq;

namespace ComparisonTool.Core.Comparison.Analysis
{
    public class MinHash
    {
        private readonly int _numHashes;
        private readonly int[] _hashSeeds;

        public MinHash(int numHashes = 64)
        {
            _numHashes = numHashes;
            var rand = new Random(42);
            _hashSeeds = Enumerable.Range(0, numHashes).Select(_ => rand.Next()).ToArray();
        }

        public int[] ComputeSignature(IEnumerable<string> set)
        {
            var signature = new int[_numHashes];
            Array.Fill(signature, int.MaxValue);

            foreach (var item in set)
            {
                for (int i = 0; i < _numHashes; i++)
                {
                    int hash = item.GetHashCode() ^ _hashSeeds[i];
                    if (hash < signature[i])
                        signature[i] = hash;
                }
            }
            return signature;
        }

        public double EstimateJaccard(int[] sig1, int[] sig2)
        {
            int equal = 0;
            for (int i = 0; i < _numHashes; i++)
                if (sig1[i] == sig2[i]) equal++;
            return (double)equal / _numHashes;
        }
    }
}
