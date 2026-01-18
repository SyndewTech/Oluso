namespace Oluso.Core.Domain.Entities;

public class ClientAllowedUser
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string SubjectId { get; set; } = default!;
    public string? DisplayName { get; set; }
}
