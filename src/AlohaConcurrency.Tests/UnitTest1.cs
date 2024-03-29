using System.Diagnostics;

namespace AlohaConcurrency.Tests;

public class UnitTest1
{
    [Theory]
    [InlineData(100)]
    public async void VerifyConcurrency(int count)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        var tasks = new List<Task>();
        for (int i = 0; i < count; i++)
        {
            var i1 = i;
            tasks.Add(Task.Run(async () =>
            {
                await Task.Delay(new Random().Next(RepoForRemoteResource.SlotSizeMs));
                await new RepoForRemoteResource().Add(i1);
            }));
        }
        await Task.WhenAll(tasks);

        stopwatch.Stop();

        Assert.True(RepoForRemoteResource.ResourceOnRemoteServerInMemoryMock.List.Count == count);
        Assert.True(stopwatch.ElapsedMilliseconds < count * RepoForRemoteResource.SlotSizeMs * 1.1);
    }
}
