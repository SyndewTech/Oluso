import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { ToastService } from '@syndew/oluso-ui-core';
import { AuditLogsPageComponent } from './audit-logs-page.component';
import { AuditLogService } from '../../services/audit-log.service';
import { ClientService } from '../../services/client.service';
import { UserService } from '../../services/user.service';
import { AuditLog, AuditLogFilter } from '../../models/audit-log.model';

describe('AuditLogsPageComponent', () => {
  const log: AuditLog = {
    id: 1,
    timestamp: '2026-10-09T10:00:00Z',
    action: 'CreateUser',
    category: 'User',
    subjectId: 'u1',
    success: true,
    ipAddress: '203.0.113.7',
    originKind: 'service_client',
    originId: 'payroll',
  };

  function setup() {
    const getLogs = vi.fn((_: AuditLogFilter) => of({ items: [log], totalCount: 1, page: 1, pageSize: 50, totalPages: 1 }));
    TestBed.configureTestingModule({
      imports: [AuditLogsPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuditLogService, useValue: { getLogs, export: vi.fn() } },
        { provide: ClientService, useValue: { getClients: () => of([{ id: 1, clientId: 'payroll', clientName: 'Payroll sync' }]) } },
        {
          provide: UserService,
          useValue: {
            getUsers: () => of({ items: [], totalCount: 0, page: 1, pageSize: 50, totalPages: 0 }),
            getUser: () => of(null),
          },
        },
        { provide: ToastService, useValue: { error: vi.fn() } },
      ],
    });
    const fixture = TestBed.createComponent(AuditLogsPageComponent);
    fixture.detectChanges();
    return { fixture, getLogs };
  }

  it('shows the origin kind and the service client by name', () => {
    const { fixture } = setup();

    const row = (fixture.nativeElement as HTMLElement).querySelector('tbody tr')!;
    expect(row.textContent).toContain('Service client');
    expect(row.textContent).toContain('Payroll sync');
  });

  it('filters by origin kind and then by the chosen actor', () => {
    const { fixture, getLogs } = setup();
    const page = fixture.componentInstance;

    const select = document.createElement('select');
    select.innerHTML = '<option value="service_client"></option>';
    select.value = 'service_client';
    page.onOriginKindChange({ target: select } as unknown as Event);
    page.onOriginIdChange('payroll');

    expect(getLogs).toHaveBeenLastCalledWith(
      expect.objectContaining({ originKind: 'service_client', originId: 'payroll', pageNumber: 1 })
    );
  });

  it('narrows to everything one actor did from a log row', () => {
    const { fixture, getLogs } = setup();

    fixture.componentInstance.filterByOrigin(log);

    expect(getLogs).toHaveBeenLastCalledWith(expect.objectContaining({ originKind: 'service_client', originId: 'payroll' }));
  });
});
