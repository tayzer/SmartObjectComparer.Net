using Microsoft.VisualStudio.TestTools.UnitTesting;

// These are integration tests that launch worker processes and open named pipes.
// Running them in parallel lets those OS resources contend, so this assembly runs
// its tests serially.
[assembly: DoNotParallelize]
