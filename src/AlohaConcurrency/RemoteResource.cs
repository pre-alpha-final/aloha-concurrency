namespace AlohaConcurrency;

public class RemoteResource
{
    public Guid Version { get; set; } = Guid.NewGuid();
    public List<int> List { get; set; } = new();
}
