using Newtonsoft.Json;

namespace AlohaConcurrency;

public class RepoForRemoteResource
{
    // Has to fit load, save, concurrency-wait, load, verify
    // For this in-memory example could be way smaller
    // For real-life examples might need to be bigger
    // The bigger the slot the bigger the confidence in concurrency at cost of lost time (one action per slot)
    public const int SlotSizeMs = 1000;
    private const int MaxTimeSlip = 100;

    public static RemoteResource ResourceOnRemoteServerInMemoryMock = new RemoteResource();

    public async Task Add(int item)
    {
        await Task.Delay(1);

        // Single loop has to fit in SlotSizeMs
        while (true)
        {
            // Best effort slot sync
            await Task.Delay(SlotSizeMs - (int)((DateTime.Now.Ticks / 10000) % SlotSizeMs));
            if ((int)((DateTime.Now.Ticks / 10000) % SlotSizeMs) > MaxTimeSlip)
            {
                continue;
            }

            // Load from remote server
            var loadedResource = JsonConvert.DeserializeObject<RemoteResource>(JsonConvert.SerializeObject(ResourceOnRemoteServerInMemoryMock));

            // Version for concurrency verification
            var newVersion = Guid.NewGuid();

            // Idempotent action. AlohaConcurrency is at-least-once
            if (loadedResource!.List.Contains(item) == false)
            {
                loadedResource.List.Add(item);
            }

            // Save to remote server (has to be atomic)
            ResourceOnRemoteServerInMemoryMock = new()
            {
                Version = newVersion,
                List = loadedResource.List,
            };

            // Concurrency depends on all of the above finishing for all concurrent clients
            // before we finish this instruction
            await Task.Delay(SlotSizeMs / 2);

            // Load from remote server again
            var reloadedList = JsonConvert.DeserializeObject<RemoteResource>(JsonConvert.SerializeObject(ResourceOnRemoteServerInMemoryMock));

            // Verify our version is saved
            if (reloadedList.Version == newVersion)
            {
                break;
            }
        }
    }
}
