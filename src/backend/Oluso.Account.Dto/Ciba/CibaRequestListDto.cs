namespace Oluso.Account.Dto.Ciba;

public class CibaRequestListDto
{
    public List<CibaRequestDto> Requests { get; set; } = new();
    public int TotalCount { get; set; }
}
