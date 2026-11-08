import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of } from 'rxjs';
import { ApiScopeService } from './api-scope.service';
import { ApiService } from './api.service';
import { ApiScopeDto } from '../models/api-scope.model';

describe('ApiScopeService.getAllScopes', () => {
  const scope = (name: string) => ({ name, enabled: true }) as ApiScopeDto;

  it('reads every page of the paged list endpoint and returns the scopes as one array', async () => {
    const pages = [
      { items: [scope('a'), scope('b')], totalCount: 3, page: 1, pageSize: 200, totalPages: 2 },
      { items: [scope('c')], totalCount: 3, page: 2, pageSize: 200, totalPages: 2 },
    ];
    const getPaginated = vi.fn((_: string, p: { page: number }) => of(pages[p.page - 1]));
    TestBed.configureTestingModule({ providers: [ApiScopeService, { provide: ApiService, useValue: { getPaginated } }] });

    const all = await firstValueFrom(TestBed.inject(ApiScopeService).getAllScopes());

    expect(Array.isArray(all)).toBe(true);
    expect(all.map((s) => s.name)).toEqual(['a', 'b', 'c']);
    expect(getPaginated).toHaveBeenCalledTimes(2);
  });
});
