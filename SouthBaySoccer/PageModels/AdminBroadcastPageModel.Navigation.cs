namespace SouthBaySoccer.PageModels;

public partial class AdminBroadcastPageModel : IQueryAttributable
{
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("groupId", out var value) && Guid.TryParse(value?.ToString(), out var id))
        {
            InitialGroupId = id;
        }
    }
}
