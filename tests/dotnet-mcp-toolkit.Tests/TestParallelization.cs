using Xunit;

// Tests that write a fake executable (FakeDockerExecutable) and immediately run it can hit ETXTBSY on Linux
// when another test forks a process at the same moment: the child inherits the still-open write handle, so the
// exec fails and the runner reports "docker executable not found". Running tests one at a time avoids the race.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
