/**
 * Standard paginated response wrapper.
 * Matches the backend PagedResult<T> from Oluso.Core.Api.
 */
export interface PaginatedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}
