namespace Citra.Application.Common.Paging;

/// <summary>One page of a longer list, plus what the UI needs to draw a paginator.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
