// A saturated thread pool, as a parallel test run can make it: every worker blocked, no more allowed.
ThreadPool.GetMinThreads(out var minWorkers, out var minIo);
ThreadPool.SetMaxThreads(minWorkers, minIo);
var gate = new ManualResetEventSlim();
for (var i = 0; i < minWorkers * 2; i++)
    ThreadPool.QueueUserWorkItem(_ => gate.Wait());
Thread.Sleep(500);
var onPool = Task.Run(() => "serialized");
Console.WriteLine($"Task.Run started within 5 s: {onPool.Wait(TimeSpan.FromSeconds(5))}");
var ownThread = Task.Factory.StartNew(() => "serialized", CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
Console.WriteLine($"LongRunning started within 5 s: {ownThread.Wait(TimeSpan.FromSeconds(5))}");
gate.Set();
