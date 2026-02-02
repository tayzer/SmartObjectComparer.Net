// <copyright file="MinHash.cs" company="PlaceholderCompany">



namespace ComparisonTool.Core.Comparison.Analysis {
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public class MinHash {
        private readonly int numHashes;
        private readonly int[] hashSeeds;

        public MinHash(int numHashes = 64) {
            this.numHashes = numHashes;
            var rand = new Random(42);
            this.hashSeeds = Enumerable.Range(0, numHashes).Select(_ => rand.Next()).ToArray();
        }

        public int[] ComputeSignature(IEnumerable<string> set) {
            var signature = new int[this.numHashes];
            Array.Fill(signature, int.MaxValue);

            foreach (var item in set) {
                for (var i = 0; i < this.numHashes; i++) {
                    var hash = item.GetHashCode() ^ this.hashSeeds[i];
                    if (hash < signature[i]) {
                        signature[i] = hash;
                    }
                }
            }

            return signature;
        }

        public double EstimateJaccard(int[] sig1, int[] sig2) {
            var equal = 0;
            for (var i = 0; i < this.numHashes; i++) {
                if (sig1[i] == sig2[i]) {
                    equal++;
                }
            }

            return (double)equal / this.numHashes;
        }
    }
}
