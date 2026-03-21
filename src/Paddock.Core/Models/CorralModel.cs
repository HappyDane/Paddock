namespace Paddock.Core.Models;

public class CorralModel
{
    public string Name { get; set; } = "default";
    public List<PaddockModel> Paddocks { get; set; } = new();
}
